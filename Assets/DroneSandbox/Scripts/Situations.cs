using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // What the enemy is doing in a sector. Each situation, with a sector and a seed, makes a mission: where its
    // units are, what has to be destroyed, the drones you get and the briefing.
    public enum Situation
    {
        SupplyConvoy, HiddenArtillery, AmmoDepot, BridgeCrossing, AAScreen, JammingNode, TroopRotation, DroneLaunchSite,
        CommandPost, RepairYard, FuelDepot, RadarStation, ArmouredColumn, RocketBattery, TankLaager, ObservationPost,
        MortarLine, NightResupply, LogisticsHub, RiverFerry, EngineerColumn, DecoyField, RailSupply, AirfieldStrike,
        RetreatingColumn, DugInTanks, AirDefenceBelt, WoodlandDump, CommandConvoy, Breakthrough,
        IdentificationHunt,
    }

    public enum Daylight { Dawn, Day, Dusk, Night }
    public enum Weather { Clear, Overcast, Rain, Fog }

    public static class Situations
    {
        public static readonly string[] Names =
        {
            "Supply Convoy", "Hidden Artillery", "Ammo Depot", "Bridge Crossing", "AA Screen", "Jamming Node", "Troop Rotation",
            "Drone Launch Site", "Command Post", "Repair Yard", "Fuel Depot", "Radar Station", "Armoured Column", "Rocket Battery",
            "Tank Laager", "Observation Post", "Mortar Line", "Night Resupply", "Logistics Hub", "River Ferry", "Engineer Column",
            "Decoy Field", "Rail Supply", "Airfield Strike", "Retreating Column", "Dug-in Tanks", "Air Defence Belt",
            "Supply Dump in the Woods", "Command Convoy", "Breakthrough",
            "Identification Hunt",
        };

        public static string Name(Situation s) => Names[(int)s];

        public static float Hour(Daylight d) => d == Daylight.Dawn ? 5.6f : d == Daylight.Day ? 12.5f : d == Daylight.Dusk ? 20.3f : 23.4f;

        // Whether this situation can happen in this kind of sector (some need a river, a railway or an airstrip).
        public static bool Fits(Situation s, SectorType t)
        {
            switch (s)
            {
                case Situation.BridgeCrossing:
                case Situation.RiverFerry:
                case Situation.EngineerColumn:
                    return t.river != RiverStyle.None;
                case Situation.RailSupply:
                    return (t.features & SectorFeature.Rail) != 0;
                case Situation.AirfieldStrike:
                    return (t.features & SectorFeature.Airstrip) != 0;
                case Situation.IdentificationHunt:
                    return t.roads != RoadStyle.Tracks || t.villages >= 2
                        || (t.features & (SectorFeature.Industry | SectorFeature.Rail | SectorFeature.Airstrip)) != 0;
                default:
                    return true;
            }
        }

        public static Mission Generate(SectorPlan plan, Situation situation, int seed, Daylight daylight)
        {
            MapLayout.Prepare();
            var b = new Builder(plan, situation, seed, daylight);
            return b.Build();
        }

        class Builder
        {
            readonly SectorPlan plan;
            readonly Situation situation;
            readonly System.Random rng;
            readonly Mission m;
            readonly Vector2 basePos, hqPos, fwd, mid;

            public Builder(SectorPlan sector, Situation s, int seed, Daylight daylight)
            {
                plan = sector;
                situation = s;
                rng = new System.Random(seed * 92821 + (int)s * 7 + 3);
                basePos = MapLayout.Flat(sector.basePosition);
                hqPos = MapLayout.Flat(sector.enemyHqPosition);
                fwd = (hqPos - basePos).normalized;
                mid = (basePos + hqPos) / 2f;
                m = new Mission
                {
                    name = Name(s),
                    startHour = Hour(daylight),
                    key = $"sector.{sector.kind}.{sector.seed}.{s}",
                };
            }

            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

            public Mission Build()
            {
                switch (situation)
                {
                    case Situation.SupplyConvoy:
                    {
                        var route = RouteIn(out string from);
                        Convoy(route, (EnemyType.Tank, false), (EnemyType.Truck, true), (EnemyType.Truck, true), (EnemyType.Truck, true), (EnemyType.Truck, true));
                        Guns(RoutePoint(route, 0.55f), 1, false, 14f);
                        Stock(2, 2, 5, 5);
                        Brief($"A supply convoy of four trucks led by a tank is coming in {from} towards the Dravin command post. Stop every truck before it arrives.",
                            "A fuel truck reached the Dravin command post.");
                        break;
                    }
                    case Situation.HiddenArtillery:
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Artillery, Wild(15f, 70f), true, true);
                        Guns(EnemySide(20f, 50f), 1, true, 12f);
                        Stock(3, 2, 4, 5);
                        Brief("Three guns under camouflage nets are shelling your lines. Search the yellow circles with Scouts, then strike what you find.");
                        break;
                    case Situation.AmmoDepot:
                    {
                        Vector2 c = NearVillageOrWild(out string place);
                        Unit(EnemyType.Depot, Open(c, 12f), true);
                        Unit(EnemyType.Depot, Open(c, 16f), true);
                        Unit(EnemyType.Truck, Open(c, 14f), false);
                        Unit(EnemyType.Truck, Open(c, 14f), false);
                        Guns(c, 2, true, 18f);
                        Stock(2, 3, 4, 5);
                        Brief($"Two ammunition depots are stocked {place}. Blow them both up; the radar makes the AA guns reach further.");
                        break;
                    }
                    case Situation.BridgeCrossing:
                    {
                        Vector2 bridge = plan.bridges.Count > 0 ? plan.bridges[rng.Next(plan.bridges.Count)] : RoadNear(mid);
                        var route = RouteThrough(bridge, out string from);
                        Convoy(route, (EnemyType.APC, true), (EnemyType.Tank, true), (EnemyType.APC, true), (EnemyType.Tank, true));
                        Guns(bridge + fwd * 22f, 1, false, 8f);
                        Stock(2, 2, 6, 6);
                        Brief($"An armoured column is driving in {from} to force the bridge over the {plan.riverName}. Stop all four vehicles before they get across.",
                            "The column crossed the river.");
                        break;
                    }
                    case Situation.AAScreen:
                        for (int i = 0; i < 5; i++) Unit(EnemyType.AAGun, EnemySide(-5f + i * 10f, 5f + i * 10f, wide: true), true);
                        Unit(EnemyType.Radar, EnemySide(15f, 35f), false);
                        Unit(EnemyType.Radar, EnemySide(25f, 45f), false);
                        Stock(2, 4, 5, 7);
                        Brief("A screen of five AA guns covers the Dravin lines, backed by two radars. Tear a hole in it: destroy every gun.");
                        break;
                    case Situation.JammingNode:
                    {
                        Vector2 c = EnemySide(20f, 45f);
                        Unit(EnemyType.Jammer, Open(c, 10f), true);
                        Unit(EnemyType.Jammer, Open(c + Side() * 40f, 10f), true);
                        Guns(c, 2, true, 16f);
                        Stock(2, 4, 4, 6);
                        Brief("Two jammer trucks are cutting the links of every drone that flies into their circles. Hit them from outside: Strike drones' missiles outrange the jamming.");
                        break;
                    }
                    case Situation.TroopRotation:
                    {
                        var route = RouteIn(out string from);
                        Convoy(route, (EnemyType.APC, true), (EnemyType.Truck, true), (EnemyType.APC, true), (EnemyType.Truck, true), (EnemyType.APC, true));
                        Stock(2, 2, 6, 6);
                        Brief($"Fresh Dravin troops are rotating in {from}: three APCs and two trucks. Stop them all before they reach the command post.",
                            "The fresh troops reached the front.");
                        break;
                    }
                    case Situation.DroneLaunchSite:
                    {
                        Vector2 c = EnemySide(25f, 55f);
                        Unit(EnemyType.Rocket, Open(c, 10f), true);
                        Unit(EnemyType.Rocket, Open(c, 12f), true);
                        Unit(EnemyType.Depot, Open(c, 14f), true);
                        Unit(EnemyType.Truck, Open(c, 14f), false);
                        Guns(c, 2, false, 18f);
                        Stock(2, 3, 5, 6);
                        Brief("Dravin launch trucks are firing attack drones at your rear from a field site. Destroy both launchers and their ammunition.");
                        break;
                    }
                    case Situation.CommandPost:
                        m.units.Add(new MissionUnit { type = EnemyType.Command, target = true });
                        foreach (var d in plan.defences)
                            if (Vector3.Distance(d.position, plan.enemyHqPosition) < 20f)
                                m.units.Add(new MissionUnit { type = d.type, position = d.position });
                        Stock(2, 4, 6, 8);
                        Brief("Destroy the Dravin command post. A radar and three AA guns defend it: open a way in before you send the heavy hitters.");
                        break;
                    case Situation.RepairYard:
                    {
                        Vector2 c = NearVillageOrWild(out string place);
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Tank, Open(c, 14f), true);
                        Unit(EnemyType.Truck, Open(c, 14f), false);
                        Unit(EnemyType.Truck, Open(c, 14f), false);
                        Guns(c, 1, false, 16f);
                        Stock(2, 3, 5, 5);
                        Brief($"Three damaged tanks are being repaired {place}. Finish them off before they're back in the fight.");
                        break;
                    }
                    case Situation.FuelDepot:
                    {
                        Vector2 c = EnemySide(15f, 50f);
                        for (int i = 0; i < 4; i++) Unit(EnemyType.Tanker, Open(c, 14f), true);
                        Unit(EnemyType.Depot, Open(c, 10f), false);
                        Guns(c, 2, true, 18f);
                        Stock(2, 2, 5, 5);
                        Brief("Four fuel tankers are parked at a forward fuel point. Burn them all and the Dravin armour runs dry.");
                        break;
                    }
                    case Situation.RadarStation:
                    {
                        Vector2 c = High(EnemySide(20f, 60f));
                        Unit(EnemyType.Radar, Open(c, 6f), true);
                        Unit(EnemyType.Radar, Open(c + Side() * 30f, 6f), true);
                        Guns(c, 3, false, 16f);
                        Stock(2, 4, 4, 5);
                        Brief("Two radars on the high ground give every AA gun around them extra reach. Knock both out.");
                        break;
                    }
                    case Situation.ArmouredColumn:
                    {
                        var route = RouteIn(out string from);
                        Convoy(route, (EnemyType.Tank, true), (EnemyType.Tank, true), (EnemyType.APC, false), (EnemyType.Tank, true), (EnemyType.Tank, true));
                        Stock(2, 3, 6, 7);
                        Brief($"Four tanks with an APC are moving up {from}. Destroy all four tanks before they reach the command post. Two Kamikaze hits kill a tank.",
                            "The tanks reached the front.");
                        break;
                    }
                    case Situation.RocketBattery:
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Rocket, Wild(20f, 60f, fields: true), true, i == 2);
                        Guns(EnemySide(25f, 45f), 1, true, 14f);
                        Stock(2, 3, 5, 5);
                        Brief("A battery of three rocket launchers is set up in the fields. One is under a net: search for it.");
                        break;
                    case Situation.TankLaager:
                    {
                        Vector2 c = Wild(20f, 60f);
                        for (int i = 0; i < 4; i++) Unit(EnemyType.Tank, Open(c, 16f), true, true);
                        Guns(c, 1, false, 20f);
                        Stock(3, 2, 6, 6);
                        Brief("A tank company has pulled into the woods to rest, hidden under the trees. Find all four tanks and destroy them.");
                        break;
                    }
                    case Situation.ObservationPost:
                    {
                        Vector2 c = High(EnemySide(0f, 40f));
                        Unit(EnemyType.Radar, Open(c, 4f), true);
                        Unit(EnemyType.Truck, Open(c, 8f), true);
                        Guns(c, 1, false, 12f);
                        Stock(1, 2, 3, 3);
                        Brief("An observation post on the high ground is calling artillery onto your lines. Take out the radar and the truck.");
                        break;
                    }
                    case Situation.MortarLine:
                    {
                        Vector2 c = Wild(10f, 50f), along = Side();
                        for (int i = 0; i < 4; i++) Unit(EnemyType.Artillery, c + along * (i - 1.5f) * 10f, true, i % 2 == 0);
                        Guns(c - fwd * 10f, 1, false, 8f);
                        Stock(3, 2, 5, 5);
                        Brief("Four guns stand in a line behind a tree line, two of them under nets. Silence the whole line.");
                        break;
                    }
                    case Situation.NightResupply:
                    {
                        m.startHour = 23.4f;
                        var route = RouteIn(out string from);
                        Convoy(route, (EnemyType.Truck, true), (EnemyType.Truck, true), (EnemyType.Tanker, true), (EnemyType.Truck, true), (EnemyType.Truck, true));
                        Stock(2, 2, 6, 6);
                        Brief($"Under cover of darkness, a resupply convoy is driving in {from}. Thermal cameras (the Wren-T or the Nightjar) see their engines glow.",
                            "The resupply got through.");
                        break;
                    }
                    case Situation.LogisticsHub:
                    {
                        Vector2 c = NearVillageOrWild(out string place);
                        Unit(EnemyType.Depot, Open(c, 14f), true);
                        Unit(EnemyType.Depot, Open(c, 16f), true);
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Truck, Open(c, 16f), true);
                        Guns(c, 1, true, 20f);
                        Stock(2, 3, 6, 7);
                        Brief($"The Dravin have turned {place} into a logistics hub: two depots and three trucks. Destroy them all, and leave the houses be.");
                        break;
                    }
                    case Situation.RiverFerry:
                    {
                        Vector2 bank = Riverbank();
                        for (int i = 0; i < 4; i++) Unit(EnemyType.Truck, Open(bank, 10f), true);
                        Unit(EnemyType.APC, Open(bank, 10f), false);
                        Guns(bank + fwd * 15f, 1, false, 8f);
                        Stock(2, 2, 5, 5);
                        Brief($"Trucks are queueing on the bank of the {plan.riverName} for a ferry. Catch all four before they get across.");
                        break;
                    }
                    case Situation.EngineerColumn:
                    {
                        Vector2 bridge = plan.bridges.Count > 0 ? plan.bridges[0] : RoadNear(mid);
                        var route = RouteTo(bridge, out string from);
                        Convoy(route, (EnemyType.APC, false), (EnemyType.Truck, true), (EnemyType.Truck, true), (EnemyType.Truck, true));
                        Stock(2, 2, 5, 5);
                        Brief($"Engineer trucks are heading {from} to the crossing on the {plan.riverName}. Stop the three trucks before they reach it.",
                            "The engineers reached the crossing.");
                        break;
                    }
                    case Situation.DecoyField:
                    {
                        Vector2 c = EnemySide(15f, 50f);
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Tank, Open(c, 22f), true);
                        for (int i = 0; i < 4; i++) m.units.Add(new MissionUnit { type = EnemyType.Tank, position = V3(Open(c, 24f)), yaw = R(0f, 360f), decoy = true });
                        Guns(c, 1, false, 20f);
                        Stock(3, 2, 5, 5);
                        Brief("Seven tanks are parked in the open, but only three are real: the rest are inflatable decoys. Fly a Scout close to tell them apart, and don't waste drones.");
                        break;
                    }
                    case Situation.RailSupply:
                    {
                        Vector2 c = StationOrRail();
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Truck, Open(c, 14f), true);
                        Unit(EnemyType.Tanker, Open(c, 14f), true);
                        Guns(c, 2, false, 16f);
                        Stock(2, 3, 5, 5);
                        Brief("A supply train has just unloaded at the railway. Destroy the four vehicles taking the cargo forward.");
                        break;
                    }
                    case Situation.AirfieldStrike:
                    {
                        Vector2 c = plan.airstripCenter + new Vector2(-plan.airstripAlong.y, plan.airstripAlong.x) * Mathf.Sign(Vector2.Dot(hqPos - plan.airstripCenter, new Vector2(-plan.airstripAlong.y, plan.airstripAlong.x))) * -24f;
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Tanker, Open(c, 16f), true);
                        Unit(EnemyType.Depot, Open(c, 12f), true);
                        Guns(plan.airstripCenter, 3, true, 30f);
                        Stock(2, 4, 5, 6);
                        Brief("The Dravin are flying supplies into the airstrip. Destroy the fuel tankers and the ammunition stacked beside the runway.");
                        break;
                    }
                    case Situation.RetreatingColumn:
                    {
                        var route = RouteIn(out string from);
                        route.Reverse();
                        Convoy(route, (EnemyType.Truck, true), (EnemyType.Tank, true), (EnemyType.Truck, true), (EnemyType.Tank, true));
                        Stock(2, 2, 6, 6);
                        Brief($"A Dravin column is pulling back from the command post, heading out {Reverse(from)}. Stop all four vehicles before they get away.",
                            "The column got away.");
                        break;
                    }
                    case Situation.DugInTanks:
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Tank, Wild(5f, 35f), true, true);
                        Guns(EnemySide(15f, 35f), 1, false, 14f);
                        Stock(3, 2, 6, 6);
                        Brief("Three tanks are dug in at the edge of the woods, waiting to ambush. Find and destroy them.");
                        break;
                    case Situation.AirDefenceBelt:
                        for (int i = 0; i < 4; i++) Unit(EnemyType.AAGun, EnemySide(-5f + i * 12f, 10f + i * 12f, wide: true), true);
                        Unit(EnemyType.Radar, EnemySide(20f, 40f), true);
                        Unit(EnemyType.Radar, EnemySide(30f, 55f), true);
                        Unit(EnemyType.Jammer, EnemySide(15f, 40f), true);
                        Stock(3, 5, 6, 9);
                        Brief("A full air defence belt: four AA guns, two radars and a jammer. Destroy all seven to open the sky over the sector.");
                        break;
                    case Situation.WoodlandDump:
                    {
                        Vector2 c = Wild(15f, 60f);
                        for (int i = 0; i < 3; i++) Unit(EnemyType.Depot, Open(c, 26f), true, true);
                        Guns(c, 1, false, 18f);
                        Stock(3, 3, 4, 5);
                        Brief("Three supply dumps are hidden in the woods. Search with Scouts and destroy every one.");
                        break;
                    }
                    case Situation.CommandConvoy:
                    {
                        var route = RouteIn(out string from);
                        Convoy(route, (EnemyType.Tank, false), (EnemyType.APC, true), (EnemyType.Tank, false));
                        Guns(RoutePoint(route, 0.5f), 1, false, 12f);
                        Stock(2, 2, 4, 3);
                        Brief($"A Dravin general is travelling {from} in an APC between two tanks. Destroy the APC before it reaches the command post.",
                            "The general got through.");
                        break;
                    }
                    case Situation.Breakthrough:
                    {
                        var route = RouteToBase();
                        Convoy(route, (EnemyType.Tank, true), (EnemyType.APC, true), (EnemyType.Tank, true), (EnemyType.APC, true), (EnemyType.Tank, true), (EnemyType.APC, true));
                        Stock(2, 4, 8, 9);
                        Brief($"The Dravin are attacking: six armoured vehicles are coming down the road at {Factions.PlayerBase}. Stop every one before it reaches you.",
                            "The attack broke through.");
                        break;
                    }
                    case Situation.IdentificationHunt:
                    {
                        Vector2 c = Open(RoadNear(EnemySide(15f, 40f)), 18f);
                        int count = rng.Next(3, 6), targets = rng.Next(1, 3), remaining = targets;
                        // Keep each group all empties or all inflatables: the recon messages distinguish them per mission.
                        bool empty = rng.Next(2) == 0;
                        EnemyType type = empty ? EnemyType.Tanker : EnemyType.Truck;
                        for (int i = 0; i < count; i++)
                        {
                            Vector2 p = Open(c, 18f);
                            // Choose without replacement, so neither the first contact nor the parking order gives it away.
                            bool target = rng.Next(count - i) < remaining;
                            if (target) remaining--;
                            m.units.Add(new MissionUnit
                            {
                                type = type, position = V3(p), yaw = R(0f, 360f), target = target,
                                decoy = !target, empty = !target && empty,
                            });
                        }
                        // One hit per target, plus one spare for a mistaken strike; no AA interrupts the observation.
                        Stock(2, 0, targets + 1, targets + 1);
                        m.contactRadius = 20f;
                        string vehicles = empty ? "fuel tankers" : "supply trucks";
                        string real = targets == 1 ? "one is a real target" : "two are real targets";
                        string falseTargets = empty ? "empty" : "inflatable decoys";
                        Brief($"{count} similar Dravin {vehicles} are parked together beside the road, but only {real}; the rest are {falseTargets}. " +
                            "Fly a Scout over the contact circles and watch each vehicle for a couple of seconds to confirm which ones are real before striking. " +
                            "Spend Kamikazes only on the real targets, then bring the Scouts home. You have one spare Kamikaze.");
                        break;
                    }
                }
                Camera();
                return m;
            }

            // ---- Units ----

            void Unit(EnemyType type, Vector2 at, bool target, bool hidden = false) =>
                m.units.Add(new MissionUnit { type = type, position = V3(at), yaw = R(0f, 360f), target = target, hidden = hidden });

            // AA guns round a point, and a radar beside them.
            void Guns(Vector2 c, int count, bool radar, float spread)
            {
                for (int i = 0; i < count; i++) Unit(EnemyType.AAGun, Open(c, spread + 6f), false);
                if (radar) Unit(EnemyType.Radar, Open(c, spread), false);
            }

            void Convoy(List<Vector3> route, params (EnemyType type, bool target)[] vehicles)
            {
                m.convoyRoute = route;
                for (int i = 0; i < vehicles.Length; i++)
                    m.units.Add(new MissionUnit { type = vehicles[i].type, target = vehicles[i].target, convoy = i + 1 });
            }

            void Stock(int scouts, int strikes, int kamikazes, int par)
            {
                m.scouts = scouts;
                m.strikes = strikes;
                m.kamikazes = kamikazes;
                m.par = par;
            }

            void Brief(string text, string escape = null)
            {
                m.briefing = $"{plan.name}, {plan.Type.name.ToLowerInvariant()}. " + text;
                if (escape != null) m.escapeFailure = escape;
            }

            // The camera starts over the targets (or the middle of the convoy route), looking from your side.
            void Camera()
            {
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (var u in m.units)
                {
                    // Frame the whole hunt group, so the opening camera does not point out the real targets.
                    if (!u.target && (situation != Situation.IdentificationHunt || !u.decoy)) continue;
                    Vector3 p = u.convoy > 0 && m.convoyRoute != null ? m.convoyRoute[Mathf.Min(m.convoyRoute.Count - 1, 24)] : u.position;
                    if (u.type == EnemyType.Command) p = plan.enemyHqPosition;
                    sum += p;
                    n++;
                }
                m.cameraFocus = n > 0 ? sum / n : plan.enemyHqPosition;
                m.cameraYaw = Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg;
                m.cameraDistance = 100f;
            }

            // ---- Where things go ----

            Vector2 Side() => new Vector2(-fwd.y, fwd.x);
            static Vector3 V3(Vector2 v) => new Vector3(v.x, 0f, v.y);

            // A point on open ground on the enemy's side, between minF and maxF metres beyond the middle.
            Vector2 EnemySide(float minF, float maxF, bool wide = false)
            {
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    Vector2 p = mid + fwd * R(minF, maxF) + Side() * R(wide ? -80f : -55f, wide ? 80f : 55f);
                    if (OpenGround(p, 4f)) return p;
                }
                return mid + fwd * maxF + Side() * R(-30f, 30f);
            }

            // Somewhere wild on the enemy's side (woods, scrub, meadow), or out in the fields.
            Vector2 Wild(float minF, float maxF, bool fields = false)
            {
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    Vector2 p = mid + fwd * R(minF, maxF) + Side() * R(-70f, 70f);
                    if (!OpenGround(p, 4f)) continue;
                    if (fields ? MapLayout.InField(p, 0f) : MapLayout.IsWild(p, 0f)) return p;
                }
                return EnemySide(minF, maxF);
            }

            // A point within `spread` of c on open ground (or, failing that, within twice as far).
            Vector2 Open(Vector2 c, float spread)
            {
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    float a = R(0f, Mathf.PI * 2f), r = R(3f, attempt < 40 ? spread : spread * 2f);
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (OpenGround(p, 2.5f)) return p;
                }
                return c + new Vector2(R(-6f, 6f), R(-6f, 6f));
            }

            bool OpenGround(Vector2 p, float margin)
            {
                if (Mathf.Abs(p.x) > 120f || Mathf.Abs(p.y) > 120f) return false;
                if (MapLayout.LakeDistance(p.x, p.y) < margin || MapLayout.NearDam(p, margin)) return false;
                if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + margin) return false;
                if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 1f) return false;
                if (MapLayout.OnWorks(p, 0f) || Vector2.Distance(p, basePos) < MapLayout.BaseRadius + 25f) return false;
                if (Undergrowth.UnderTrees(p, 7f)) return false; // seen from a drone, not lost under a canopy
                float spacing = situation == Situation.IdentificationHunt ? 12f : 5f; // inspect look-alikes separately, outside one blast
                foreach (var u in m.units) // and not parked on top of another unit
                    if ((new Vector2(u.position.x, u.position.z) - p).sqrMagnitude < spacing * spacing) return false;
                return !MapLayout.NearFlatSite(p, 1f) || Vector2.Distance(p, hqPos) < MapLayout.HqRadius + 25f;
            }

            // The highest open ground near p.
            Vector2 High(Vector2 p)
            {
                Vector2 best = p;
                float top = Ground.HeightAt(V3(p));
                for (int k = 0; k < 30; k++)
                {
                    Vector2 q = p + new Vector2(R(-40f, 40f), R(-40f, 40f));
                    float h = Ground.HeightAt(V3(q));
                    if (h > top && OpenGround(q, 4f))
                    {
                        top = h;
                        best = q;
                    }
                }
                return best;
            }

            // A village on the enemy's side (and its name), or a wild spot.
            Vector2 NearVillageOrWild(out string place)
            {
                var options = new List<int>();
                for (int i = 0; i < plan.villages.Count; i++)
                    if (Vector2.Dot(plan.villages[i] - mid, fwd) > -10f && Mathf.Abs(plan.villages[i].x) < 120f && Mathf.Abs(plan.villages[i].y) < 120f)
                        options.Add(i);
                if (options.Count > 0)
                {
                    int i = options[rng.Next(options.Count)];
                    place = (plan.farmsteads[i] ? "at " : "in ") + plan.villageNames[i];
                    // Just outside the houses, on open ground.
                    Vector2 v = plan.villages[i];
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        float a = R(0f, Mathf.PI * 2f);
                        Vector2 p = v + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(26f, 40f);
                        if (OpenGround(p, 6f)) return p;
                    }
                    return v;
                }
                place = "in the woods";
                return Wild(20f, 55f);
            }

            Vector2 Riverbank()
            {
                var line = MapLayout.RiverField.points;
                for (int attempt = 0; attempt < 80 && line.Count > 2; attempt++)
                {
                    int i = rng.Next(1, line.Count - 1);
                    Vector2 dir = (line[i + 1] - line[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                    if (Vector2.Dot(side, fwd) < 0f) side = -side;
                    Vector2 p = line[i] + side * (MapLayout.RiverHalfWidth + 8f);
                    bool nearBridge = false;
                    foreach (var b in plan.bridges) if (Vector2.Distance(b, p) < 30f) nearBridge = true;
                    if (!nearBridge && OpenGround(p, 3f)) return p;
                }
                return EnemySide(0f, 20f);
            }

            Vector2 StationOrRail()
            {
                foreach (var s in plan.sites)
                    if (s.model == "Scenery_Station") return Open(s.position, 22f);
                var rails = MapLayout.RailLines;
                if (rails.Count > 0) return Open(rails[0][rails[0].Count / 2], 20f);
                return EnemySide(10f, 40f);
            }

            Vector2 RoadNear(Vector2 p)
            {
                Vector2 best = p;
                float d = float.MaxValue;
                foreach (var line in MapLayout.RoadLines)
                    foreach (var q in line)
                        if ((q - p).sqrMagnitude < d)
                        {
                            d = (q - p).sqrMagnitude;
                            best = q;
                        }
                return best;
            }

            // ---- Routes along the roads ----

            // From a road at the map's edge on the enemy side to the command post. `from` says where it comes in.
            List<Vector3> RouteIn(out string from)
            {
                Vector2 entry = Entry(out from);
                return RoadRoute.Find(entry, HqDoor());
            }

            List<Vector3> RouteTo(Vector2 to, out string from)
            {
                Vector2 entry = Entry(out from);
                return RoadRoute.Find(entry, to);
            }

            // From the enemy side, over the bridge, and on into your side.
            List<Vector3> RouteThrough(Vector2 bridge, out string from)
            {
                Vector2 entry = Entry(out from);
                var route = RoadRoute.Find(entry, bridge);
                var beyond = RoadRoute.Find(bridge, Beyond(bridge));
                if (beyond.Count > 1) route.AddRange(beyond.GetRange(1, beyond.Count - 1));
                return route;
            }

            // Down the road from the command post to your base.
            List<Vector3> RouteToBase()
            {
                var route = RoadRoute.Find(HqDoor(), basePos + fwd * (MapLayout.BaseRadius + 4f));
                return route;
            }

            Vector2 HqDoor() => hqPos - fwd * (MapLayout.HqRadius - 4f);

            // A road point some 60 m past the bridge on your side.
            Vector2 Beyond(Vector2 bridge)
            {
                Vector2 best = bridge - fwd * 50f;
                float score = float.MaxValue;
                foreach (var line in MapLayout.RoadLines)
                    foreach (var q in line)
                    {
                        float s = Mathf.Abs(Vector2.Distance(q, bridge) - 60f) + (Vector2.Dot(q - bridge, fwd) > -20f ? 1000f : 0f);
                        if (s < score)
                        {
                            score = s;
                            best = q;
                        }
                    }
                return best;
            }

            // A road point near the terrain's edge, as far towards the enemy as possible.
            Vector2 Entry(out string from)
            {
                Vector2 best = hqPos + fwd * 120f;
                float score = float.MinValue;
                foreach (var line in MapLayout.RoadLines)
                    foreach (var q in line)
                    {
                        float edge = Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y));
                        if (edge < 185f || edge > 225f) continue;
                        float s = Vector2.Dot(q - mid, fwd) + R(0f, 60f);
                        if (s > score)
                        {
                            score = s;
                            best = q;
                        }
                    }
                from = "from the " + Compass(best) + " edge of the sector";
                return best;
            }

            static string Compass(Vector2 p)
            {
                float a = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;
                string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
                return names[Mathf.RoundToInt(((a + 360f) % 360f) / 45f) % 8];
            }

            static string Reverse(string from) => from.Replace("from the", "towards the");

            Vector2 RoutePoint(List<Vector3> route, float t)
            {
                if (route == null || route.Count == 0) return mid;
                var p = route[Mathf.Clamp(Mathf.RoundToInt(t * (route.Count - 1)), 0, route.Count - 1)];
                return Open(new Vector2(p.x, p.z), 12f);
            }
        }
    }

    // Shortest paths along the roads: every road point is a node, joined to its neighbours along the road and to
    // points of other roads within a few metres (junctions, bridges, tracks branching off).
    public static class RoadRoute
    {
        static List<Vector2> nodes;
        static List<List<(int to, float cost)>> links;
        static List<List<Vector2>> builtFor;

        static void Prepare()
        {
            var lines = MapLayout.RoadLines;
            if (builtFor == lines) return;
            builtFor = lines;
            nodes = new List<Vector2>();
            links = new List<List<(int, float)>>();
            var cells = new Dictionary<long, List<int>>();
            long Key(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 4f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 4f);
            foreach (var line in lines)
            {
                for (int i = 0; i < line.Count; i++)
                {
                    int id = nodes.Count;
                    nodes.Add(line[i]);
                    links.Add(new List<(int, float)>());
                    if (i > 0)
                    {
                        float d = Vector2.Distance(line[i - 1], line[i]);
                        links[id].Add((id - 1, d));
                        links[id - 1].Add((id, d));
                    }
                }
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                long k = Key(nodes[i]);
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                int cx = Mathf.FloorToInt(nodes[i].x / 4f), cz = Mathf.FloorToInt(nodes[i].y / 4f);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list)) continue;
                        foreach (int j in list)
                        {
                            if (j <= i + 2 && j >= i - 2) continue;
                            float d = Vector2.Distance(nodes[i], nodes[j]);
                            if (d < 3.5f) links[i].Add((j, d + 0.5f));
                        }
                    }
            }
        }

        // The road route from the road point nearest `from` to the one nearest `to`, as ground points; a straight
        // line when they aren't joined.
        public static List<Vector3> Find(Vector2 from, Vector2 to)
        {
            Prepare();
            var route = new List<Vector3>();
            if (nodes.Count == 0)
            {
                route.Add(new Vector3(from.x, 0f, from.y));
                route.Add(new Vector3(to.x, 0f, to.y));
                return route;
            }
            int a = Nearest(from), b = Nearest(to);
            var dist = new float[nodes.Count];
            var prev = new int[nodes.Count];
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = float.MaxValue;
                prev[i] = -1;
            }
            dist[a] = 0f;
            var open = new SortedSet<(float, int)> { (0f, a) };
            while (open.Count > 0)
            {
                var (d, i) = open.Min;
                open.Remove(open.Min);
                if (i == b) break;
                if (d > dist[i]) continue;
                foreach (var (j, cost) in links[i])
                {
                    float nd = d + cost;
                    if (nd >= dist[j]) continue;
                    if (dist[j] < float.MaxValue) open.Remove((dist[j], j));
                    dist[j] = nd;
                    prev[j] = i;
                    open.Add((nd, j));
                }
            }
            if (prev[b] < 0 && a != b)
            {
                route.Add(new Vector3(from.x, 0f, from.y));
                route.Add(new Vector3(to.x, 0f, to.y));
                return route;
            }
            var path = new List<int>();
            for (int i = b; i >= 0; i = prev[i]) path.Add(i);
            path.Reverse();
            foreach (int i in path) route.Add(new Vector3(nodes[i].x, 0f, nodes[i].y));
            return route;
        }

        static int Nearest(Vector2 p)
        {
            int best = 0;
            float d = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float e = (nodes[i] - p).sqrMagnitude;
                if (e < d)
                {
                    d = e;
                    best = i;
                }
            }
            return best;
        }
    }
}
