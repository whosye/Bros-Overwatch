using System.Collections.Generic;
using UnityEngine;

// Strecha s dirami (postavi Editor/RoofHoleTool z puvodni sedlove strechy - hranolu, ktery se jen skryje).
// Jen data: z ceho se strecha stavi a kde jsou diry. Zmena v Inspectoru + tlacitko "Prestavet strechu".
public class RoofHoles : MonoBehaviour
{
    [Tooltip("Puvodni strecha (hranol z ProBuilderu) - skryta, slouzi jako predloha tvaru.")]
    public Transform source;
    [Tooltip("Velikost dir v metrech: podel hrebene x po svahu.")]
    public Vector2 holeSize = new Vector2(1.5f, 1.5f);
    [Tooltip("Tloustka desek strechy.")]
    public float thickness = 0.2f;

    [System.Serializable]
    public struct Hole
    {
        public int side;        // -1 / +1 = ktery svah
        public float along;     // m od zacatku hrebene
        public float down;      // m od hrebene dolu po svahu
    }

    public List<Hole> holes = new List<Hole>();
}
