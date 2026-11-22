"""Translate the imported OWM node graphs into URP surface calculations.

Keeps layered colors, vertex masks and packed normals; no texture baking,
resampling, polygon reduction or changes to the source .blend are performed.
"""
import re
from dataclasses import dataclass


def ident(name):
    return re.sub(r'[^a-zA-Z0-9_]', '_', name)


def number(value):
    text = format(float(value), '.9g')
    return text + ('.0' if '.' not in text and 'e' not in text else '')


@dataclass
class Expr:
    code: str
    size: int = 1

    def as_type(self, size):
        if size == self.size:
            return self.code
        if size == 1:
            weights = 'float3(0.2126,0.7152,0.0722)' if self.size == 4 else 'float3(0.3333333333,0.3333333333,0.3333333333)'
            return f'dot(({self.code}).xyz,{weights})'
        if self.size == 1:
            return f'float{size}(' + ','.join([self.code] * size) + ')'
        return f'({self.code}).xyz' if size == 3 else f'float4({self.code},1.0)'


def socket_size(socket):
    return 4 if socket.type == 'RGBA' else 3 if socket.type == 'VECTOR' else 1


def literal(value, size):
    if size == 1:
        return Expr(number(value))
    return Expr(f'float{size}(' + ','.join(number(v) for v in value[:size]) + ')', size)


class Compiler:
    def __init__(self):
        self.lines = []
        self.cache = {}
        self.sequence = 0
        self.vertex_layers = set()

    def emit(self, code, size=1):
        self.sequence += 1
        name = f'v{self.sequence}'
        kind = 'float' if size == 1 else f'float{size}'
        self.lines.append(f'    {kind} {name} = {code};')
        return Expr(name, size)

    def input(self, socket, env):
        if socket.is_linked:
            return self.output(socket.links[0].from_socket, env)
        return literal(socket.default_value, socket_size(socket))

    def group_env(self, node, env):
        return {s.name: self.input(s, env) for s in node.inputs if s.type != 'SHADER' and hasattr(s, 'default_value')}

    def group_output(self, node, output_name):
        output = next(n for n in node.node_tree.nodes if n.type == 'GROUP_OUTPUT' and n.is_active_output)
        return output.inputs[output_name]

    def output(self, socket, env):
        key = (socket.as_pointer(), id(env))
        if key in self.cache:
            return self.cache[key]
        node = socket.node
        kind = node.type
        size = socket_size(socket)
        get = lambda index, n=1: self.input(node.inputs[index], env).as_type(n)
        if kind == 'GROUP_INPUT':
            result = env[socket.name]
        elif kind == 'GROUP':
            group_env = self.group_env(node, env)
            # Retain environments: cache keys use their identity.
            self.environments.append(group_env)
            result = self.input(self.group_output(node, socket.name), group_env)
        elif kind == 'REROUTE':
            result = self.input(node.inputs[0], env)
        elif kind in ('VALUE', 'RGB'):
            result = literal(socket.default_value, size)
        elif kind in ('SEPXYZ', 'SEPARATE_COLOR'):
            index = list(node.outputs).index(socket)
            result = self.emit(f'({get(0, 3)}).{"xyz"[index]}')
        elif kind in ('COMBXYZ', 'COMBINE_COLOR'):
            result = self.emit('float3(' + ','.join(get(i) for i in range(3)) + ')', 3)
        elif kind == 'VERTEX_COLOR':
            self.vertex_layers.add(node.layer_name)
            if node.layer_name not in ('', 'ColorMap1'):
                raise RuntimeError('Unsupported vertex layer: ' + node.layer_name)
            result = Expr('IN.color.a') if socket.name == 'Alpha' else Expr('IN.color', 4)
        elif kind == 'LIGHT_PATH':
            # Camera evaluation. Transparent decals do not cast shadows in Unity.
            result = Expr('1.0' if socket.name == 'Is Camera Ray' else '0.0')
        elif kind == 'NORMAL_MAP':
            result = self.emit(f'SapaNormal({get("Color", 3)},{get("Strength")})', 3)
        elif kind == 'MATH':
            a, b = get(0), get(1)
            operation = node.operation
            expressions = {
                'ADD': f'({a}+{b})', 'SUBTRACT': f'({a}-{b})', 'MULTIPLY': f'({a}*{b})',
                'DIVIDE': f'SapaDivide({a},{b})', 'POWER': f'pow(max(0.0,{a}),{b})',
                'MAXIMUM': f'max({a},{b})', 'GREATER_THAN': f'({a}>{b}?1.0:0.0)',
            }
            if operation not in expressions:
                raise RuntimeError('Unsupported math: ' + operation)
            code = expressions[operation]
            result = self.emit(f'saturate({code})' if node.use_clamp else code)
        elif kind == 'VECT_MATH':
            a, b = get(0, 3), get(1, 3)
            expressions = {
                'ADD': f'({a}+{b})', 'SUBTRACT': f'({a}-{b})', 'MULTIPLY': f'({a}*{b})',
                'MULTIPLY_ADD': f'({a}*{b}+{get(2, 3)})' if len(node.inputs) > 2 else '',
                'NORMALIZE': f'SafeNormalize({a})', 'DOT_PRODUCT': f'dot({a},{b})',
            }
            if node.operation not in expressions:
                raise RuntimeError('Unsupported vector math: ' + node.operation)
            result = self.emit(expressions[node.operation], size)
        elif kind == 'MIX':
            size = 4 if node.data_type == 'RGBA' else 3 if node.data_type == 'VECTOR' else 1
            offset = {4: 6, 3: 4, 1: 2}[size]
            a, b = get(offset, size), get(offset + 1, size)
            factor = get(0)
            if node.clamp_factor:
                factor = f'saturate({factor})'
            blend = {'MIX': b, 'MULTIPLY': f'({a}*{b})', 'ADD': f'({a}+{b})'}[node.blend_type]
            code = f'lerp({a},{blend},{factor})'
            result = self.emit(f'saturate({code})' if node.clamp_result else code, size)
        else:
            raise RuntimeError(f'Unsupported OWM node: {kind}, {node.name}')
        self.cache[key] = result
        return result

    def surface(self, socket, env):
        node = socket.links[0].from_node
        if node.type == 'GROUP':
            group_env = self.group_env(node, env)
            self.environments.append(group_env)
            return self.surface(self.group_output(node, socket.links[0].from_socket.name), group_env)
        if node.type == 'MIX_SHADER':
            factor = self.input(node.inputs[0], env).as_type(1)
            a, b = self.surface(node.inputs[1], env), self.surface(node.inputs[2], env)
            name = f's{len(self.lines)}'
            self.lines.append(f'    SapaSurface {name} = SapaMix({a},{b},saturate({factor}));')
            return name
        name = f's{len(self.lines)}'
        if node.type == 'BSDF_TRANSPARENT':
            self.lines.append(f'    SapaSurface {name} = SapaEmpty(); {name}.alpha = 0.0;')
            return name
        if node.type != 'BSDF_PRINCIPLED':
            raise RuntimeError('Unsupported surface: ' + node.type)
        fields = {
            'color': ('Base Color', 3), 'metallic': ('Metallic', 1), 'roughness': ('Roughness', 1),
            'alpha': ('Alpha', 1), 'emission': ('Emission Color', 3),
        }
        values = {key: self.input(node.inputs[s], env).as_type(n) for key, (s, n) in fields.items()}
        values['emission'] = f'({values["emission"]}*{self.input(node.inputs["Emission Strength"],env).as_type(1)})'
        values['normal'] = self.input(node.inputs['Normal'], env).as_type(3) if node.inputs['Normal'].is_linked else 'float3(0,0,1)'
        self.lines.append(f'    SapaSurface {name} = SapaEmpty();')
        self.lines.extend(f'    {name}.{key} = {value};' for key, value in values.items())
        return name

    def compile(self, tree, slots):
        self.environments = []
        group_input = next(n for n in tree.nodes if n.type == 'GROUP_INPUT')
        env = {s.name: Expr(f'Sample_{ident(s.name)}(IN)', 4) for s in group_input.outputs if s.name in slots}
        self.environments.append(env)
        output = next(n for n in tree.nodes if n.type == 'GROUP_OUTPUT' and n.is_active_output)
        result = self.surface(output.inputs['Shader'], env)
        return '\n'.join(self.lines) + '\n    return ' + result + ';\n'


def generate(trees, folder):
    trees = sorted(trees, key=lambda t: t.name)
    slots = sorted({s.name for tree in trees for n in tree.nodes if n.type == 'GROUP_INPUT'
                    for s in n.outputs if s.type != 'SHADER' and s.name != ''})
    slots = [s for s in slots if s != '']
    properties = []
    declarations = []
    uniforms = []
    samplers = []
    for name in slots:
        key = 'OW_' + ident(name)
        properties.extend([
            f'        _{key}("{name}", 2D) = "white" {{}}',
            f'        _{key}Value("{name} default", Vector) = (1,1,1,1)',
            f'        _{key}Mode("{name} mode", Float) = 0',
            f'        _{key}UV("{name} UV", Float) = 0',
        ])
        declarations.append(f'TEXTURE2D(_{key});')
        uniforms.extend([f'float4 _{key}Value;',f'float _{key}Mode;',f'float _{key}UV;'])
        samplers.append(f'''float4 Sample_{ident(name)}(SapaInput IN)
{{
    if (_{key}Mode < 0.5) return _{key}Value;
    float2 uv = _{key}UV < 0.5 ? IN.uv0 : (_{key}UV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_{key},sampler_LinearRepeat,uv);
    return _{key}Mode > 1.5 ? value.aaaa : value;
}}''')
    functions=[]
    for index, tree in enumerate(trees):
        compiler=Compiler()
        body=compiler.compile(tree,slots)
        functions.append(f'// {tree.name}\nSapaSurface SapaStyle{index}(SapaInput IN)\n{{\n{body}}}')
    dispatcher=['SapaSurface EvaluateSapa(SapaInput IN) {']
    dispatcher.extend(f'    if (_Style < {i}.5) return SapaStyle{i}(IN);' for i in range(len(trees)-1))
    dispatcher.append(f'    return SapaStyle{len(trees)-1}(IN);\n}}')
    helpers='''// Generated from the original OWM Blender node graphs. Do not hand-edit.
struct SapaInput { float2 uv0, uv1, uv2; float4 color; };
struct SapaSurface { float3 color, normal, emission; float metallic, roughness, alpha; };
float SapaDivide(float a,float b) { return b == 0.0 ? 0.0 : a/b; }
float3 SapaNormal(float3 color,float strength) { float3 n=color*2.0-1.0; n.xy*=strength; return SafeNormalize(n); }
SapaSurface SapaEmpty() { SapaSurface s=(SapaSurface)0; s.normal=float3(0,0,1); s.alpha=1; s.roughness=1; return s; }
SapaSurface SapaMix(SapaSurface a,SapaSurface b,float f) { SapaSurface s; s.color=lerp(a.color,b.color,f); s.normal=SafeNormalize(lerp(a.normal,b.normal,f)); s.emission=lerp(a.emission,b.emission,f); s.metallic=lerp(a.metallic,b.metallic,f); s.roughness=lerp(a.roughness,b.roughness,f); s.alpha=lerp(a.alpha,b.alpha,f); return s; }
'''
    # URP includes GlobalSamplers.hlsl through Core.hlsl.
    source=helpers+'\n'+'\n'.join(declarations)+'''
CBUFFER_START(UnityPerMaterial)
float _Style, _Cutoff, _DebugSurface;
float4 _BaseMap_ST, _BaseColor;
'''+ '\n'.join(uniforms)+'\nCBUFFER_END\n'+'\n'.join(samplers+functions+dispatcher)
    (folder/'SapaSurface.hlsl').write_text(source,encoding='utf8')
    template=(__import__('pathlib').Path(__file__).with_name('overwatch_shader.template')).read_text(encoding='utf8')
    (folder/'Sapa.shader').write_text(template.replace('/* GENERATED_PROPERTIES */','\n'.join(properties)),encoding='utf8')
    return {tree.name:index for index,tree in enumerate(trees)}, slots
