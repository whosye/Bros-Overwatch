"""Regression checks for the user's playtest: separate contacts and preserve choices."""
import json
import array
from pathlib import Path
import unittest
import wave

from export_to_unity import cues_for, assemble_shells, RATE, APP, UNITY


class FeedbackExportTests(unittest.TestCase):
    def test_wall_contact_and_body_contact_never_share_a_random_pool(self):
        self.assertEqual(cues_for('pova_weapon', 'knife_hit2.wav'), ['pova_weapon.impact.surface'])
        self.assertEqual(cues_for('pova_weapon', 'knife_stab.wav'), ['pova_weapon.impact.body'])
        self.assertEqual(cues_for('max_weapon', 'knife_swish_light_01.mp3'), ['max_weapon.swing'])
        self.assertEqual(cues_for('max_weapon', 'knife_hit_light_flesh_01.mp3'), ['max_weapon.impact.body'])

    def test_rock_impact_is_a_landing_not_an_explosion(self):
        self.assertEqual(cues_for('honza_ult', 'boulder_impact_hard4.wav'), ['honza_ult.land'])

    def test_entry_and_landing_are_not_walking_steps(self):
        for name, role in [('land_concrete_01.wav', 'land'), ('concrete_ct_01.wav', 'step')]:
            self.assertEqual(cues_for('steps_stone', name), ['steps_stone.' + role])
        self.assertEqual(cues_for('steps_water', 'water_entry_01.wav'), ['steps_water.enter'])
        self.assertEqual(cues_for('world_splash', 'water_exit_01.wav'), ['steps_water.exit'])

    def test_feedback_reload_has_exactly_five_shell_insertions(self):
        settings = json.loads((APP / 'integration_overrides.json').read_text(encoding='utf-8'))
        shell = settings['reload_shells']['honza_reload']
        self.assertEqual(shell['repeats'], 5)
        self.assertIn('nova_insertshell', shell['source'])
        self.assertNotIn('pump', shell['source'])
        with wave.open(str(UNITY / 'Assets/Audio/Collected/Weapons/Reload/honza_reload.wav')) as audio:
            self.assertGreater(audio.getnframes() / audio.getframerate(), 1.4)

    def test_five_complete_insertions_fit_without_overlap_or_tail_truncation(self):
        # A long source must speed up, rather than being cut or covering the next insertion.
        part = array.array('f', [0.5]) * RATE
        mix = assemble_shells(part, 5, 1.8)
        self.assertEqual(len(mix), round(RATE * 1.8))
        slot = int(RATE * (1.8 - 0.08) / 5)
        length = int(slot * 0.9)
        chunks = []
        for index in range(5):
            start = round(RATE * 0.04) + slot * index
            chunks.append(mix[start:start + length])
            self.assertGreater(max(chunks[-1]), 0.4)
            self.assertEqual(max(abs(v) for v in mix[start + length:start + slot]), 0)
        self.assertTrue(all(chunk == chunks[0] for chunk in chunks))
        self.assertEqual(chunks[-1][-1], 0)

    def test_requested_weapon_models_and_scope_modes_have_distinct_pools(self):
        settings = json.loads((APP / 'integration_overrides.json').read_text(encoding='utf-8'))['defaults']
        self.assertTrue(all('m4a1_silencer_' in p for p in settings['david_fire']))
        self.assertTrue(all('revolver-' in p for p in settings['sindel_fire']))
        self.assertTrue(all('/awp/' in p for p in settings['kondulic_fire_scoped']))
        self.assertTrue(all('/mp5/' in p for p in settings['kondulic_fire']))
        bank = (UNITY / 'Assets/Resources/CollectedAudioBank.asset').read_text(encoding='utf-8')
        for cue in ('david_fire', 'sindel_fire', 'kondulic_fire_scoped', 'kondulic_fire'):
            self.assertIn('  - id: ' + cue + '\n', bank)

    def test_export_did_not_change_saved_user_choices(self):
        state = json.loads((APP / 'collection.json').read_text(encoding='utf-8'))
        exported = json.loads((UNITY / 'Assets/Audio/Collected/IMPORT_MANIFEST.json').read_text(encoding='utf-8'))
        self.assertEqual(state['revision'], exported['revision'])
        for item, choice in state['items'].items():
            if choice.get('files') and not choice.get('skipped'):
                self.assertEqual(choice['files'], exported['items'][item]['sources'])


if __name__ == '__main__': unittest.main()
