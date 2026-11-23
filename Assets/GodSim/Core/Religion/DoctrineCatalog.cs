using System.Collections.Generic;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// The 16 doctrines (4 axes x 4). Built once, immutable afterwards. Effects are per
    /// <see cref="ReligionSystem"/> run (48 ticks) and clamped again by the system.
    /// </summary>
    public static class DoctrineCatalog
    {
        /// <summary>Number of axes (Worship, Ethos, Practice, View).</summary>
        public const int AxisCount = 4;

        private static readonly DoctrineDefinition[] _all;
        private static readonly DoctrineDefinition[][] _byAxis;
        private static readonly string[] _axisNames = { "worship", "ethos", "practice", "view" };
        private static readonly string[] _axisDisplay = { "Worship", "Ethos", "Practice", "View of the God" };
        private static readonly string[] _memoryKeys =
        {
            ReligionKeys.DoctrinePrefix + "worship", ReligionKeys.DoctrinePrefix + "ethos",
            ReligionKeys.DoctrinePrefix + "practice", ReligionKeys.DoctrinePrefix + "view"
        };

        static DoctrineCatalog()
        {
            var list = new List<DoctrineDefinition>(16);
            const DoctrineTrait P = DoctrineTrait.Piety, A = DoctrineTrait.Aggression, C = DoctrineTrait.Curiosity, M = DoctrineTrait.Mercantilism;
            const DoctrineTrait Gentle = DoctrineTrait.Gentleness, Trad = DoctrineTrait.Tradition, Frugal = DoctrineTrait.Frugality, Skeptic = DoctrineTrait.Skepticism;
            const DoctrineTrait Kind = DoctrineTrait.Benevolence, Wrath = DoctrineTrait.Wrath, Aloof = DoctrineTrait.Aloofness, Ambi = DoctrineTrait.Ambivalence;
            const DoctrineTrait Sacred = DoctrineTrait.SacredLand;

            // ------------------------------------------------------------ Worship
            list.Add(new DoctrineDefinition(DoctrineId.SunWorship, DoctrineAxis.Worship, "sun_worship", "Sun Worship")
            {
                Description = "The god is seen in the sun's fire; dawn rites kindle zeal in every hearth.",
                Tenet = "the unblinking eye of the sun",
                FaithNudge = 0.006f, UnrestNudge = -0.002f, BaseWeight = 0.2f,
            }.W(P, 0.6f).W(A, 0.3f));
            list.Add(new DoctrineDefinition(DoctrineId.AncestorVeneration, DoctrineAxis.Worship, "ancestor_veneration", "Ancestor Veneration")
            {
                Description = "The honoured dead intercede with the god; tradition binds the living together.",
                Tenet = "the whispering voices of the ancestors",
                FaithNudge = 0.003f, UnrestNudge = -0.004f, ConversionResistance = 0.4f, BaseWeight = 0.2f,
            }.W(P, 0.4f).W(Trad, 0.5f));
            list.Add(new DoctrineDefinition(DoctrineId.NatureSpirits, DoctrineAxis.Worship, "nature_spirits", "Nature Spirits")
            {
                Description = "Every grove and river is a vessel of the divine; the faithful live gently on the land.",
                Tenet = "the spirits of root, river and stone",
                FaithNudge = 0.004f, HappinessNudge = 0.004f,
            }.W(P, 0.5f).W(Frugal, 0.5f).W(Sacred, 0.15f));
            list.Add(new DoctrineDefinition(DoctrineId.CelestialOrder, DoctrineAxis.Worship, "celestial_order", "Celestial Order")
            {
                Description = "The heavens turn by divine law; astronomers read the god's will in the stars.",
                Tenet = "the turning wheel of the stars",
                FaithNudge = 0.002f, UnrestNudge = -0.004f, ResearchPerFaith = 0.03f,
            }.W(C, 0.7f).W(M, 0.3f));

            // ------------------------------------------------------------ Ethos
            list.Add(new DoctrineDefinition(DoctrineId.Mercy, DoctrineAxis.Ethos, "mercy", "Mercy")
            {
                Description = "Charity and forgiveness are the highest worship; the poor are fed at the temple door.",
                Tenet = "an open hand to the hungry",
                HappinessNudge = 0.004f, UnrestNudge = -0.006f, MissionaryZeal = 1.1f,
            }.W(Gentle, 0.6f).W(Kind, 0.3f).W(P, 0.1f));
            list.Add(new DoctrineDefinition(DoctrineId.MartialZeal, DoctrineAxis.Ethos, "martial_zeal", "Martial Zeal")
            {
                Description = "Faith is proven by the sword; warrior-priests drill the young for holy war.",
                Tenet = "the blessed blade",
                FaithNudge = 0.004f, UnrestNudge = -0.002f, DefenseBonus = 0.1f, MissionaryZeal = 1.2f,
            }.W(A, 0.8f).W(Wrath, 0.3f));
            list.Add(new DoctrineDefinition(DoctrineId.Asceticism, DoctrineAxis.Ethos, "asceticism", "Asceticism")
            {
                Description = "Want is purity; fasting and silence armour the soul against doubt.",
                Tenet = "the purity of want",
                FaithNudge = 0.008f, HappinessNudge = -0.004f, HeresyMultiplier = 0.75f, ConversionResistance = 0.3f,
            }.W(P, 0.5f).W(Frugal, 0.4f));
            list.Add(new DoctrineDefinition(DoctrineId.ProsperityGospel, DoctrineAxis.Ethos, "prosperity_gospel", "Prosperity Gospel")
            {
                Description = "Wealth is a sign of divine favour; merchants tithe gladly and preach in the markets.",
                Tenet = "the god's gold in an honest purse",
                FaithNudge = -0.002f, HappinessNudge = 0.006f, HeresyMultiplier = 1.1f, MissionaryZeal = 1.3f,
            }.W(M, 0.8f).W(Kind, 0.1f));

            // ------------------------------------------------------------ Practice
            list.Add(new DoctrineDefinition(DoctrineId.Pilgrimage, DoctrineAxis.Practice, "pilgrimage", "Pilgrimage")
            {
                Description = "Every believer must walk to the holy site once in life; the roads hum with pilgrims.",
                Tenet = "the long road to the holy site",
                FaithNudge = 0.002f, HolySiteFaith = 0.006f, PilgrimageHappiness = 0.006f,
            }.W(P, 0.5f).W(M, 0.3f).W(Sacred, 0.3f));
            list.Add(new DoctrineDefinition(DoctrineId.Monasticism, DoctrineAxis.Practice, "monasticism", "Monasticism")
            {
                Description = "Cloistered orders copy, study and pray; their scriptoria feed the civilization's learning.",
                Tenet = "the patient scholarship of the cloister",
                FaithNudge = 0.004f, ResearchPerFaith = 0.1f, ResearchPerSettlement = 0.02f, HeresyMultiplier = 0.9f,
            }.W(C, 0.8f).W(P, 0.2f));
            list.Add(new DoctrineDefinition(DoctrineId.Sacrifice, DoctrineAxis.Practice, "sacrifice", "Sacrifice")
            {
                Description = "Blood and harvest are offered on the altar; the god must be fed to stay its hand.",
                Tenet = "smoke rising from the altar",
                FaithNudge = 0.01f, HappinessNudge = -0.006f, FlatDevotion = 0.004f, DefenseBonus = 0.03f,
            }.W(A, 0.5f).W(P, 0.3f).W(Wrath, 0.3f));
            list.Add(new DoctrineDefinition(DoctrineId.Festivals, DoctrineAxis.Practice, "festivals", "Festivals")
            {
                Description = "Feast days crowd the calendar; worship is song, dance and shared bread.",
                Tenet = "the joyous feast-days",
                FaithNudge = 0.002f, HappinessNudge = 0.008f, UnrestNudge = -0.008f,
            }.W(M, 0.5f).W(Gentle, 0.3f).W(Kind, 0.2f));

            // ------------------------------------------------------------ View of the God
            list.Add(new DoctrineDefinition(DoctrineId.LovingParent, DoctrineAxis.View, "loving_parent", "Loving Parent")
            {
                Description = "The god cherishes mortals like children; blessings deepen love, but wrath feels like betrayal.",
                Tenet = "a parent's tender care",
                HappinessNudge = 0.003f, BlessingDevotion = 0.012f, WrathDevotion = -0.012f, BaseWeight = 0.25f,
            }.W(Kind, 0.9f).W(Gentle, 0.2f));
            list.Add(new DoctrineDefinition(DoctrineId.WrathfulJudge, DoctrineAxis.View, "wrathful_judge", "Wrathful Judge")
            {
                Description = "The god weighs every sin and strikes the wicked; fear keeps the faithful obedient.",
                Tenet = "the judge whose thunder punishes sin",
                HappinessNudge = -0.003f, UnrestNudge = -0.004f, BlessingDevotion = 0.004f, WrathDevotion = 0.012f, BaseWeight = 0.15f,
            }.W(Wrath, 1f).W(A, 0.2f));
            list.Add(new DoctrineDefinition(DoctrineId.DistantWatcher, DoctrineAxis.View, "distant_watcher", "Distant Watcher")
            {
                Description = "The god watches from afar and rarely meddles; the faith is calm, sober and slow to change.",
                Tenet = "the silent watcher beyond the sky",
                BlessingDevotion = 0.003f, WrathDevotion = -0.003f, HeresyMultiplier = 0.9f, ConversionResistance = 0.2f, BaseWeight = 0.2f,
            }.W(Aloof, 0.4f).W(C, 0.2f));
            list.Add(new DoctrineDefinition(DoctrineId.Trickster, DoctrineAxis.View, "trickster", "Trickster")
            {
                Description = "The god's gifts and curses are riddles; any sign of divine attention thrills the faithful.",
                Tenet = "the laughing god of riddles",
                HappinessNudge = 0.002f, BlessingDevotion = 0.006f, WrathDevotion = 0.006f, HeresyMultiplier = 1.15f, BaseWeight = 0.1f,
            }.W(Ambi, 0.5f).W(Skeptic, 0.2f));

            _all = list.ToArray();
            _byAxis = new DoctrineDefinition[AxisCount][];
            for (int a = 0; a < AxisCount; a++)
            {
                var axisList = new List<DoctrineDefinition>(4);
                for (int i = 0; i < _all.Length; i++) if ((int)_all[i].Axis == a) axisList.Add(_all[i]);
                _byAxis[a] = axisList.ToArray();
            }
        }

        /// <summary>Every doctrine, indexed by (int)DoctrineId.</summary>
        public static IReadOnlyList<DoctrineDefinition> All => _all;

        /// <summary>Number of doctrines.</summary>
        public static int Count => _all.Length;

        /// <summary>Doctrines of one axis, in id order.</summary>
        public static IReadOnlyList<DoctrineDefinition> OfAxis(DoctrineAxis axis)
        {
            int a = (int)axis;
            return a >= 0 && a < AxisCount ? _byAxis[a] : new DoctrineDefinition[0];
        }

        /// <summary>The definition for an id (null if out of range).</summary>
        public static DoctrineDefinition Get(DoctrineId id) => TryGet((int)id, out var d) ? d : null;

        /// <summary>Looks up a doctrine by numeric id (e.g. read back from memory).</summary>
        public static bool TryGet(int id, out DoctrineDefinition def)
        {
            def = id >= 0 && id < _all.Length ? _all[id] : null;
            return def != null;
        }

        /// <summary>Lowercase axis key used in memory keys ("worship", "ethos", "practice", "view").</summary>
        public static string AxisKey(DoctrineAxis axis) { int a = (int)axis; return a >= 0 && a < AxisCount ? _axisNames[a] : "unknown"; }

        /// <summary>Display name of an axis.</summary>
        public static string AxisName(DoctrineAxis axis) { int a = (int)axis; return a >= 0 && a < AxisCount ? _axisDisplay[a] : "Unknown"; }

        /// <summary>Civilization.Memory key holding the doctrine id of an axis ("rel.doctrine.&lt;axis&gt;").</summary>
        public static string MemoryKey(DoctrineAxis axis) { int a = (int)axis; return a >= 0 && a < AxisCount ? _memoryKeys[a] : ReligionKeys.DoctrinePrefix + "unknown"; }

        /// <summary>
        /// True when two views of the god are opposed (Loving Parent vs Wrathful Judge, Distant Watcher vs Trickster).
        /// Used for holy wars.
        /// </summary>
        public static bool ViewsOppose(DoctrineId a, DoctrineId b)
        {
            return (a == DoctrineId.LovingParent && b == DoctrineId.WrathfulJudge) || (a == DoctrineId.WrathfulJudge && b == DoctrineId.LovingParent)
                || (a == DoctrineId.DistantWatcher && b == DoctrineId.Trickster) || (a == DoctrineId.Trickster && b == DoctrineId.DistantWatcher);
        }
    }
}
