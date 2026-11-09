using UnityEngine; namespace DroneSandbox {
    public class Hotspot
    {
        public SectorKind kind;
        public int seed;
        public Situation situation;
        public Daylight daylight;
        public Weather weather;
        public float heat;          // 0-1: how much enemy activity
        public Vector2 mapPosition; // on the theatre map, 0-1 across and up
        public string sectorName;

        public bool final;          // the campaign's last operation
        public SectorPlan Plan() => SectorGenerator.Generate(kind, seed);
        public string Key => $"hotspot.{kind}.{seed}";
    }

}