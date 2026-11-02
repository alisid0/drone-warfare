using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum TechId { ThermalCamera, Nightjar, Armour, Motors, Warheads, Optics, Supply }

    public struct TechItem
    {
        public TechId id;
        public string name, info;
        public int cost;
    }

    // The tech shop. Destroying Dravin units in missions earns points, which buy new drones and upgrades.
    // Points and purchases are saved in PlayerPrefs and kept from mission to mission. Free play has all the tech.
    public static class Tech
    {
        public const int PointsPerNewStar = 20;
        public const float ArmourBonus = 1.4f, MotorBonus = 1.2f, WarheadBonus = 1.3f;
        public const float OpticsSpotRange = 36f;

        public static readonly TechItem[] Items =
        {
            new TechItem { id = TechId.ThermalCamera, cost = 50, name = "Wren-T thermal camera",
                info = "Scouts carry a thermal camera: press T to see heat at night." },
            new TechItem { id = TechId.Nightjar, cost = 150, name = "Nightjar heavy bomber",
                info = "One per mission, counts as two drones for stars. Three bombs, rearms at base, own thermal camera. Key 4." },
            new TechItem { id = TechId.Armour, cost = 80, name = "Armoured frames",
                info = "Every drone takes 40% more damage before it goes down." },
            new TechItem { id = TechId.Motors, cost = 70, name = "High-output motors",
                info = "Every drone flies 20% faster." },
            new TechItem { id = TechId.Warheads, cost = 100, name = "Heavier warheads",
                info = "Guns, missiles, kamikaze charges and bombs hit 30% harder." },
            new TechItem { id = TechId.Optics, cost = 60, name = "Long-range optics",
                info = $"Drones spot hidden targets from {OpticsSpotRange:0} m instead of {SandboxGame.SpotRange:0} m." },
            new TechItem { id = TechId.Supply, cost = 120, name = "Supply contract",
                info = "One extra Scout, Strike and Kamikaze drone in every mission." },
        };

        static int points;
        static readonly HashSet<TechId> owned = new HashSet<TechId>();
        static bool loaded;

        public static int Points
        {
            get
            {
                Load();
                return points;
            }
        }

        public static int OwnedCount
        {
            get
            {
                Load();
                return owned.Count;
            }
        }

        // Bought in the shop. (Free play has everything; see Active.)
        public static bool Owns(TechId id)
        {
            Load();
            return owned.Contains(id);
        }

        // Whether a piece of tech is in use right now: everything in free play, what you've bought in missions.
        public static bool Active(TechId id)
        {
            var game = SandboxGame.Instance;
            return (game != null && game.Mode == GameMode.Sandbox) || Owns(id);
        }

        public static int KillValue(EnemyType type)
        {
            switch (type)
            {
                case EnemyType.Truck: return 10;
                case EnemyType.Radar: return 25;
                case EnemyType.AAGun: return 30;
                case EnemyType.Artillery: return 35;
                case EnemyType.Tank: return 40;
                case EnemyType.Command: return 100;
                case EnemyType.APC: return 30;
                case EnemyType.Rocket: return 40;
                case EnemyType.Tanker: return 20;
                case EnemyType.Jammer: return 45;
                case EnemyType.Depot: return 60;
                default: return 0;
            }
        }

        public static void Earn(int amount)
        {
            Load();
            points += amount;
            Save();
        }

        public static bool CanBuy(TechItem item) => !Owns(item.id) && Points >= item.cost;

        public static bool Buy(TechItem item)
        {
            if (!CanBuy(item)) return false;
            points -= item.cost;
            owned.Add(item.id);
            Save();
            return true;
        }

        // Test runs use a separate wallet that starts empty and is never saved.
        public static void UseTestWallet()
        {
            loaded = true;
            points = 0;
            owned.Clear();
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            points = PlayerPrefs.GetInt("tech.points", 0);
            foreach (var item in Items)
                if (PlayerPrefs.GetInt("tech." + item.id, 0) == 1) owned.Add(item.id);
        }

        static void Save()
        {
            if (!SandboxGame.SaveProgress) return;
            PlayerPrefs.SetInt("tech.points", points);
            foreach (var item in Items) PlayerPrefs.SetInt("tech." + item.id, owned.Contains(item.id) ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
