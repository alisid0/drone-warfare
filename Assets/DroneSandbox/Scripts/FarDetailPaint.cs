using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DroneSandbox
{
    // Shared ground painting for the 2 m tiles and the 13.7 m world picture; all masks carry a pixel footprint.
    public sealed partial class FarDetailData
    {
        // World setup culls vectors once per 32-pixel block and uses the same coverage painter as streaming.
        public Color32[] WorldPixels(int resolution, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var pixels = new Color32[resolution * resolution];
            float pixel = WorldPlan.Size / resolution;
            int blocks = (resolution + 31) / 32;
            // Leave CPU capacity for the player and cap simultaneous district construction on 15 GB machines.
            Parallel.For(0, blocks * blocks, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellation }, block =>
            {
                int bx = block % blocks * 32, bz = block / blocks * 32;
                float x0 = -WorldPlan.Half + bx * pixel, z0 = -WorldPlan.Half + bz * pixel;
                var lanes = Nearby(roads, x0, z0, 32f * pixel); var streams = Nearby(rivers, x0, z0, 32f * pixel);
                for (int j = bz; j < Math.Min(bz + 32, resolution); j++) for (int i = bx; i < Math.Min(bx + 32, resolution); i++)
                    {
                        float x = -WorldPlan.Half + (i + .5f) * pixel, z = -WorldPlan.Half + (j + .5f) * pixel;
                        var at = new Vector2(x, z);
                        pixels[j * resolution + i] = Paint(x, z, at, lanes, streams, pixel);
                    }
            });
            return pixels;
        }

        // A small cover-only picture is available immediately, without first-touching the whole world's farms.
        public Color32[] PreviewPixels(int resolution)
        {
            var pixels = new Color32[resolution * resolution];
            float step = WorldPlan.Size / resolution;
            for (int j = 0; j < resolution; j++) for (int i = 0; i < resolution; i++)
            {
                float x = -WorldPlan.Half + (i + .5f) * step, z = -WorldPlan.Half + (j + .5f) * step;
                Cover kind = CoverAt(x, z);
                Vector3 colour = kind == Cover.Water ? WaterColour : kind == Cover.Forest ? WoodFloor
                    : kind == Cover.Farmland ? (FarDetailPalette.Means[5] + FarDetailPalette.Means[9]) * .5f
                    : kind == Cover.Wetland ? FarDetailPalette.Means[1] : kind == Cover.Rock ? FarDetailPalette.Means[3] : FarDetailPalette.Means[0];
                pixels[j * resolution + i] = FarDetailPalette.Encode(colour);
            }
            return pixels;
        }

        // Field coordinates survive into the control map so crops are sampled along their own district's rows.
        public struct FieldSample
        {
            public int layer, use;
            public float angle, projection, across, along, width, length, side, end, districtEdge;
            public bool hedge;
        }

        // Analytic vector coverage and selective crop quadrature retain narrow features without repeating grass/cover work.
        public Color32 Paint(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null, float footprint = 2f)
        {
            return FarDetailPalette.Encode(PaintPoint(x, z, coverPosition, lanes ?? roads, streams ?? rivers, footprint));
        }

        // Feature ownership is sampled separately from colour; IDs and angles are never interpolated as numbers.
        public Color32 Control(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null)
        {
            if (features.SurfaceAt(x, z, out _)) return new Color32(2, 0, 0, 50); // a runway or yard: faint grit, like tarmac roads
            var land = this.cover.At(x, z);
            Cover cover = land.Dominant();
            if (cover == Cover.Forest) cover = Cover.Pasture;
            if (this.cover.Forest(x, z) > .5f) cover = Cover.Forest;
            float bank = Bank(x, z, streams ?? rivers, out _);
            float lakeBank = LakeBank(x, z, 2f, out _);
            if (cover == Cover.Water) cover = Cover.Pasture; // vector water below owns the visible edge
            int layer = cover == Cover.Farmland ? 4 : GroundLayer(cover, x, z); float angle = 0f;
            byte strength = 255;
            if (land.Weight(Cover.Farmland) > 0f)
            {
                var f = FieldAt(x, z); layer = f.layer; angle = f.angle;
                if (f.side < 0f || f.end < 0f || f.use > 2) layer = 4;
            }
            // A garden painted over farmland must also suppress that field's procedural crop rows.
            if (villages.At(new Vector2(x, z), out var garden))
            {
                garden.Coordinates(new Vector2(x, z), out float u, out float v);
                int ground = garden.GroundMaterial(u, v);
                layer = ground == 0 ? 4 : 2; angle = 0f;
                strength = ground == 2 ? (byte)50 : (byte)255;
            }
            if (bank < 4f) layer = 2;
            if (bank < 0f) strength = 0;
            if (lakeBank < 5f) layer = 2;
            if (lakeBank < 0f) strength = 0;
            Road(x, z, lanes ?? roads, out float edge, out int kind, out _);
            if (edge < 0f) { layer = 2; strength = kind == 0 ? (byte)50 : (byte)255; }
            int turn = (int)Math.Round(angle / Math.PI * 65535);
            return new Color32((byte)layer, (byte)(turn & 255), (byte)(turn >> 8), strength);
        }

        // Smooth coverage is measured in metres; roads win at crossings and retain their actual vector widths.
        Vector3 PaintPoint(float x, float z, Vector2 at, Segment[] lanes, Segment[] streams, float pixel)
        {
            // Landmarks' runways, aprons, yards and ballast (sRGB, in unwarped world metres) win over cover and roads.
            if (features.SurfaceAt(x, z, out Color feature)) return FarDetailPalette.Decode(feature);
            float bank = Bank(x, z, streams, out float riverWidth);
            var land = cover.At(x, z);
            Vector3 c = CoverColour(x, z, pixel, land);
            if (villages.At(new Vector2(x,z), out var plot)) c = VillageColour(x, z, plot, pixel);
            c = PaintRiver(c, bank, riverWidth, pixel);
            float lakeBank = LakeBank(x, z, pixel, out bool reservoir);
            Vector3 basin = PaintLake(c, lakeBank, reservoir, pixel);
            // Inlets and outlets stay open through the shore band; a dry rim must never cross a river mouth.
            float mouth = BandCoverage(-bank, riverWidth, pixel) * (1f - Coverage(-lakeBank, pixel));
            c = Blend(basin, c, mouth);
            Road(x, z, lanes, out float edge, out int kind, out float roadWidth);
            c = Blend(c, FarDetailPalette.Means[4], BandCoverage(2f - edge, roadWidth + 2f, pixel));
            Vector3 road = kind == 0 ? RoadColour : FarDetailPalette.Means[2];
            return Blend(c, road, BandCoverage(-edge, roadWidth, pixel));
        }

        static readonly Vector3 WaterColour = WaterAppearance.Lake;
        // Mono cannot hoist these three-channel Pow transfers out of sixteen million pixel queries.
        static readonly Vector3 RoadColour = FarDetailPalette.Decode(C(78, 78, 80));
        static readonly Vector3 HedgeColour = FarDetailPalette.Decode(C(46, 62, 36));

        // The ground of a wood seen from above, between and under its crowns: half the sector's litter, half shaded damp
        // grass (sRGB about 63, 73, 50, near ChatGPT's dense-canopy shadow). Plain litter showed brown halos round the
        // far canopy (the sector itself lays litter only under trees that stand, Ground.PaintForestFloor).
        static readonly Vector3 WoodFloor = new Vector3(.0505f, .0665f, .0335f);

        // Interpolate memberships, not category numbers; forest litter follows exactly the canopy's visual edge.
        Vector3 CoverColour(float x, float z, float pixel, FarDetailCover.Sample s)
        {
            float wood = cover.Forest(x, z);
            if (s.a == s.b && s.a == s.c && s.a == s.d)
            {
                if (s.a == Cover.Farmland) return Blend(FarmColour(x, z, pixel), WoodFloor, wood);
                if (s.a == Cover.Forest && wood == 1f) return WoodFloor;
                if (s.a != Cover.Pasture && s.a != Cover.Forest && s.a != Cover.Water) return GroundColour(s.a, x, z);
            }
            Vector3 grass = GroundColour(Cover.Pasture, x, z);
            Vector3 farm = s.Weight(Cover.Farmland) > 0f ? FarmColour(x, z, pixel) : grass;
            Vector3 Colour(Cover kind) => kind == Cover.Farmland ? farm
                : kind == Cover.Pasture || kind == Cover.Forest || kind == Cover.Town || kind == Cover.Village || kind == Cover.Water ? grass : GroundColour(kind, x, z);
            Vector3 c;
            if (s.a == s.b && s.a == s.c && s.a == s.d) c = Colour(s.a);
            else c = Blend(Blend(Colour(s.a), Colour(s.b), s.u), Blend(Colour(s.c), Colour(s.d), s.u), s.v);
            return Blend(c, WoodFloor, wood);
        }

        // Only coarse crop pixels need four ownership samples; cover, grass noise and vector masks are evaluated once.
        Vector3 FarmColour(float x, float z, float pixel)
        {
            var f = FieldAt(x, z);
            if (pixel <= 4.01f || f.layer >= 5 && Math.Min(f.side, f.end) > pixel) return FieldColour(f, pixel);
            float q = pixel * .25f;
            return (FieldColour(FieldAt(x - q, z - q), pixel * .5f) + FieldColour(FieldAt(x + q, z - q), pixel * .5f)
                + FieldColour(FieldAt(x - q, z + q), pixel * .5f) + FieldColour(FieldAt(x + q, z + q), pixel * .5f)) * .25f;
        }

        // Match Ground's grass layering and high-ground bias; the sector collar uses its actual splat weights.
        Vector3 GroundColour(Cover cover, float x, float z)
        {
            if (cover == Cover.Water) return WaterColour;
            if (cover == Cover.Rock) return FarDetailPalette.Means[3];
            if (cover == Cover.Wetland) return FarDetailPalette.Means[1];
            if (cover == Cover.Forest) return WoodFloor;
            Grass(x, z, out float wet, out float rough);
            return Blend(Blend(FarDetailPalette.Means[0], FarDetailPalette.Means[4], rough), FarDetailPalette.Means[1], wet);
        }

        // Dominant fine texture only; the macro colour retains continuous grass mixtures without extra samplers.
        int GroundLayer(Cover cover, float x, float z)
        {
            if (cover == Cover.Rock) return 3;
            if (cover == Cover.Wetland || cover == Cover.Forest) return 1;
            Grass(x, z, out float wet, out float rough);
            return wet > .4f ? 1 : rough > .3f ? 4 : 0;
        }

        // Ground.BuildSplat's wet/rough transfer; broad colour variation stays small and tied to the terrain.
        void Grass(float x, float z, out float wet, out float rough)
        {
            float drift = Noise(x / 80f + 70f, z / 80f + 20f) * .65f + Noise(x / 25f + 9f, z / 25f + 4f) * .35f;
            float high = Smooth((Height(x, z) - 1.5f * Scale.Distance) / (8f * Scale.Distance));
            float wetness = Clamp(.5f - .45f * high - .7f * (drift - .5f), 0, 1);
            wet = .8f * Smooth((wetness - .55f) / .4f); rough = .55f * Smooth((.45f - wetness) / .4f);
        }

        // One bounded parcel definition supplies the painters, sector meshes and high-frequency row orientation.
        public FieldSample FieldAt(float x, float z)
        {
            return fields.At(x, z).Sample(x, z);
        }

        // The same crop means as the sector, with footprint-integrated boundary strips rather than thresholded pixels.
        Vector3 FieldColour(FieldSample f, float pixel)
        {
            if (f.layer < 5) return FarDetailPalette.Means[4];
            Vector3 c = FarDetailPalette.Means[f.layer];
            if (f.use == 3 || f.use == 4)
            {
                float pitch = f.use == 3 ? 10f : 5f, width = f.use == 3 ? 2.8f : 1.5f;
                float row = PeriodicBand(f.across + width, pitch, 2f * width, pixel * f.projection);
                c = Blend(FarDetailPalette.Means[4], f.use == 3 ? FarDetailPalette.Means[1] * .8f : FarDetailPalette.Means[6], row);
            }
            float projection = f.projection;
            float crop = Coverage(f.side, pixel * projection) * Coverage(f.end, pixel * projection);
            float side = Math.Max(BandCoverage(-f.side - 1.5f, 1.5f, pixel * projection), BandCoverage(-f.end - 1.5f, 1.5f, pixel * projection));
            Vector3 hedge = HedgeColour;
            Vector3 margin = Blend(FarDetailPalette.Means[4], hedge, f.hedge ? side * .6f : 0f);
            return Blend(margin, c, crop);
        }

        // The exact scenery parcel supplies front lawn, rear yard, vegetable beds and its boundary lines.
        Vector3 VillageColour(float x, float z, VillagePlot p, float pixel)
        {
            p.Coordinates(new Vector2(x,z),out float u,out float v);
            if(p.square) return VillageGravel;
            float half=p.frontage*.5f;
            Vector3 c=VillageGrass;
            float yard=Coverage(v-p.setback-4f,pixel)*Coverage(Math.Min(p.depth-2f,p.setback+(p.farm?25f:12f))-v,pixel)*Coverage(half-2f-Math.Abs(u),pixel);
            c=Blend(c,p.farm?VillageGravel:VillageSoil,yard);
            if(!p.church&&!p.farm)
            {
                float beds=Coverage(-1f-u,pixel)*Coverage(u+half-2f,pixel)*Coverage(v-p.setback-15f,pixel)*Coverage(p.depth-3f-v,pixel);
                c=Blend(c,VillageSoil,beds*PeriodicBand(v-p.setback-15f,2.8f,1.6f,pixel));
            }
            float drive=BandCoverage(1.2f-Math.Abs(u),1.2f,pixel)*Coverage(p.setback+8f-v,pixel);
            c=Blend(c,VillageGravel,drive);
            float edge=Math.Min(half-Math.Abs(u),Math.Min(p.depth-v,Math.Abs(u)>2f?v:100f));
            return Blend(c,p.church||p.style==1?VillageGravel:HedgeColour,Coverage(.65f-edge,pixel));
        }

        static readonly Vector3 VillageGrass=FarDetailPalette.Decode(new Color(.24f,.29f,.13f));
        static readonly Vector3 VillageSoil=FarDetailPalette.Decode(new Color(.26f,.20f,.12f));
        static readonly Vector3 VillageGravel=FarDetailPalette.Decode(new Color(.43f,.41f,.35f));

        // Exposed for the deterministic field/mask harness.
        public Color32 Field(float x, float z) => FarDetailPalette.Encode(FieldColour(FieldAt(x, z), 2f));

        // Union distances avoid painting a polyline's bends repeatedly and darkening road junctions.
        static void Road(float x, float z, Segment[] lanes, out float edge, out int kind, out float halfWidth)
        {
            edge = float.MaxValue; kind = 0; halfWidth = 0f;
            foreach (var s in lanes)
            {
                float d = Distance(s, x, z, out float width) - width;
                if (d < edge) { edge = d; kind = s.kind; halfWidth = width; }
            }
        }

        // The nearest bank supplies river coverage, including tapered widths and narrow tributaries.
        static float Bank(float x, float z, Segment[] streams, out float halfWidth)
        {
            float bank = float.MaxValue; halfWidth = 0f;
            foreach (var s in streams)
            {
                float d = Distance(s, x, z, out float width) - width;
                if (d < bank) { bank = d; halfWidth = width; }
            }
            return bank;
        }

        // Box-filter approximation for a signed edge distance, bounded for subpixel features.
        public static float Coverage(float inside, float pixel) => Clamp(.5f + inside / Math.Max(.001f, pixel), 0, 1);

        // Subtract the opposite edge of a finite strip: a 1 m stream cannot cover half a 14 m pixel.
        public static float BandCoverage(float inside, float halfWidth, float pixel) => Coverage(inside, pixel) - Coverage(inside - 2f * halfWidth, pixel);

        // Integrate every repeated garden row in a pixel, preserving its mean even when a coarse pixel spans several rows.
        static float PeriodicBand(float at, float pitch, float width, float pixel)
        {
            float a = (at - pixel * .5f) / pitch, b = (at + pixel * .5f) / pitch, w = width / pitch;
            float ia = (float)Math.Floor(a) * w + Math.Min(Repeat(a, 1f), w);
            float ib = (float)Math.Floor(b) * w + Math.Min(Repeat(b, 1f), w);
            return Clamp((ib - ia) * pitch / Math.Max(.001f, pixel), 0f, 1f);
        }

        // Linear interpolation is essential to the colour agreement between near textures and distant mipmaps.
        static Vector3 Blend(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    }
}
