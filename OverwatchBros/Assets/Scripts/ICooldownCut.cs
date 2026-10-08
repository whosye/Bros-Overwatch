// Schopnost, jejiz zbyvajici cooldown jde zkratit (Pova: zabiti nebo asistence zkrati cooldowny o polovinu).
public interface ICooldownCut
{
    // fraction 0-1: o jaky podil zbyvajiciho casu se cooldown zkrati
    void CutCooldown(float fraction);
}
