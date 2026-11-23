using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// The default first-session tutorial copy (IMGUI rich text) and the one-time contextual tips.
    /// Pure data: no Unity types.
    /// </summary>
    public static class TutorialContent
    {
        // Step ids (stable: they are stored in the player's progress string).
        public const string Welcome = "welcome";
        public const string Camera = "camera";
        public const string Peoples = "peoples";
        public const string Inspect = "inspect";
        public const string TimeControls = "time";
        public const string ChronicleStep = "chronicle";
        public const string Mana = "mana";
        public const string Bless = "bless";
        public const string Shape = "shape";
        public const string Build = "build";
        public const string Ages = "ages";
        public const string Wrath = "wrath";
        public const string Objectives = "objectives";
        public const string Free = "free";

        /// <summary>In-game days the "watch the AI build" step waits.</summary>
        public const int WatchBuildDays = 30;

        private const string Gold = "#F2C751";
        private const string ManaHex = "#5C9EFF";
        private const string FaithHex = "#C79EFF";
        private const string GoodHex = "#76DB76";
        private const string BadHex = "#F5614F";

        /// <summary>Closing tips, also embedded in the last step's body.</summary>
        public static readonly string[] Tips =
        {
            "Pause often - plan your miracles while time stands still.",
            "A hungry town? <b>Rain</b> or <b>Bountiful Harvest</b> fills its granaries.",
            "A <b>Prophet</b> rekindles faith in a wavering town - and faith is mana.",
            "<b>Reveal Ore</b> near a Dwarf hold and watch the forges light up.",
            "<b>Inspire</b> speeds research when a people is stuck in an age.",
            "Wars end faster when one side is blessed... or struck by lightning.",
            "Save often: every world can be reloaded from the menu."
        };

        /// <summary>Creates the default ordered step list (a fresh array each call).</summary>
        public static TutorialStep[] CreateDefaultSteps()
        {
            return new[]
            {
                new TutorialStep(Welcome, "Welcome, young god",
                    "A new world has just been born, and it is <b>yours</b>.\n\n" +
                    "Five peoples are waking up on its shores. They will hunt, farm, build towns, trade and quarrel " +
                    "- all on their own. You don't command them. You <i>watch</i>, you <i>nudge</i>, and when you choose to, " +
                    "you <i>intervene</i> with divine power.\n\n" +
                    "This short guide walks you through your first session. Skip any step you like, or close it and come back later.",
                    TutorialObjective.None(), TutorialAnchors.Center),

                new TutorialStep(Camera, "Look around",
                    "Your view is your window onto the world.\n\n" +
                    "• <b>WASD</b> or the <b>arrow keys</b> pan (hold <b>Shift</b> to go faster)\n" +
                    "• <b>Middle-drag</b> pans, <b>right-drag</b> or <b>Q</b>/<b>E</b> rotates\n" +
                    "• <b>Scroll</b> (or PageUp/PageDown) zooms in and out\n" +
                    "• Click the <b>minimap</b> to jump anywhere\n\n" +
                    "Go on - take a look around.",
                    TutorialObjective.CameraMoved("Move, rotate or zoom the camera"), TutorialAnchors.Minimap),

                new TutorialStep(Peoples, "Find your peoples",
                    "Five races share this world:\n\n" +
                    "• <color=#E8C27A><b>Humans</b></color> - adaptable farmers and traders of the grasslands\n" +
                    "• <color=#8FD18B><b>Elves</b></color> - long-lived, pious scholars of the deep forest\n" +
                    "• <color=#C9A27E><b>Dwarves</b></color> - stubborn miners and smiths of the hills\n" +
                    "• <color=#E07A6A><b>Orcs</b></color> - fierce, fertile and hungry for land\n" +
                    "• <color=#7FC8B8><b>Saurians</b></color> - patient, devout fisherfolk of warm swamps and coasts\n\n" +
                    "Each begins as a humble camp. Press <b>Show me</b> to fly to them one by one.",
                    TutorialObjective.None("Optional: press Show me to visit each camp"), TutorialAnchors.Minimap,
                    true, TutorialFocus.Settlements),

                new TutorialStep(Inspect, "Know your towns",
                    "<b>Left-click</b> a settlement to select it. The <b>Inspector</b> tells you everything about it: " +
                    "population, food, happiness, health and faith.\n\n" +
                    "Watch the <b>Homeless</b> count. Every house shelters <b>3 to 5 people</b>, so a growing town always " +
                    "needs more homes - people without a roof grow unhappy and sick. And keep an eye on <b>Food</b>: " +
                    "an empty granary means hunger.",
                    TutorialObjective.SettlementSelected("Select a settlement"), TutorialAnchors.Inspector,
                    true, TutorialFocus.Settlements),

                new TutorialStep(TimeControls, "Master of time",
                    "Here, one tick is one <b>day</b>, and 96 days make a year.\n\n" +
                    "Use the time controls in the top bar to <b>pause</b> (<b>Space</b>) or change the <b>speed</b> " +
                    "(<b>+</b> / <b>-</b>) - from a gentle stroll to centuries flying by.\n\n" +
                    "Pausing is never cheating: it's the perfect moment to plan your next miracle.",
                    TutorialObjective.SpeedChanged("Pause the world or change its speed"), TutorialAnchors.TopBar),

                new TutorialStep(ChronicleStep, "The chronicle",
                    "Everything that matters is written down. The <b>Chronicle</b> records foundings, discoveries, " +
                    "wars, disasters - and your own miracles - as they happen.\n\n" +
                    "When you let a few centuries slip by, the Chronicle tells you what you missed.",
                    TutorialObjective.None(), TutorialAnchors.Chronicle),

                new TutorialStep(Mana, "Mana and faith",
                    "Miracles cost <color=" + ManaHex + "><b>mana</b></color>. Mana flows from the " +
                    "<color=" + FaithHex + "><b>faith</b></color> of your worshippers: temples, shrines and devoted towns raise it, " +
                    "neglect and terror lower it. Your pool is shown in the top bar and refills over time, up to a maximum.\n\n" +
                    "Every power also has a <b>cooldown</b>. Spend wisely - a god with an empty pool can only watch.",
                    TutorialObjective.None(), TutorialAnchors.TopBar),

                new TutorialStep(Bless, "A gift from above",
                    "Time for your first miracle. Pick <b>Bless</b> (or any other <color=" + GoodHex + "><b>Blessing</b></color>) " +
                    "in the <b>Powers</b> bar, then <b>click on a town</b>.\n\n" +
                    "Blessed people are happier, healthier and more devout - and devotion becomes faith, which becomes mana.",
                    TutorialObjective.CastCategory(GodPowerCategory.Blessing, "Cast a Blessing on a town"), TutorialAnchors.PowersBar),

                new TutorialStep(Shape, "Shape the land",
                    "Gods don't just bless - they <i>sculpt</i>. Select <b>Raise Land</b> and click along a <b>coast</b> " +
                    "to push new ground out of the sea. Fresh land gives crowded towns room to grow.\n\n" +
                    "Its sibling, <b>Lower Land</b>, carves bays and lakes. Use <b>[</b> and <b>]</b> to change the brush size.",
                    TutorialObjective.CastPower(GodPowerType.RaiseLand, "Raise land near the coast"), TutorialAnchors.PowersBar),

                new TutorialStep(Build, "Watch them build",
                    "Now simply watch. Each civilization's AI decides what its towns need and raises them " +
                    "<b>one building at a time</b>: farms when food runs short, workshops for tools, and a new " +
                    "<b>house for every 3-5 people</b>. Camps grow into villages, towns and cities.\n\n" +
                    "Let a month of in-game time pass (unpause if needed) and watch the builders at work.",
                    TutorialObjective.WaitDays(WatchBuildDays, "Let " + WatchBuildDays + " days pass"), TutorialAnchors.Inspector),

                new TutorialStep(Ages, "Ages and research",
                    "Scholars, shrines and libraries produce <b>research</b>. Discoveries unlock new buildings and bonuses, " +
                    "and once a people has enough knowledge, population and key technologies, it enters a new <b>age</b> - " +
                    "from the <b>Dawn</b> of stone tools all the way to <b>Ascension</b>, ten ages in all.\n\n" +
                    "Each race has its gifts: Elves study fast, Dwarves dig deep, Orcs multiply. Watch their houses " +
                    "change style as they climb.",
                    TutorialObjective.None(), TutorialAnchors.Inspector),

                new TutorialStep(Wrath, "Wrath has consequences",
                    "Lightning, wildfire, earthquake, meteor, plague, flood and drought: your <color=" + BadHex + "><b>wrath</b></color> " +
                    "is real, and so is its price.\n\n" +
                    "Wrathful acts pull your <b>alignment</b> toward darkness, and grieving, terrified people lose " +
                    "<b>devotion</b> - less faith, less mana. Punish an aggressor, humble a tyrant, test your favorites... " +
                    "just remember: they will remember.",
                    TutorialObjective.None(), TutorialAnchors.PowersBar),

                new TutorialStep(Objectives, "Goals for a god",
                    "There is no wrong way to be a god. But if you enjoy a challenge, open <b>Help</b> (<b>F1</b>): " +
                    "it lists every control and hotkey, plus ideas for goals - lead a people to a new age, end a war, " +
                    "grow a great city.",
                    TutorialObjective.OpenHelp("Open Help (F1)"), TutorialAnchors.Help),

                new TutorialStep(Free, "The world is yours",
                    "That's all you need. From here on it's a <b>sandbox</b>: no timer, no game over - only the story " +
                    "you and your peoples write together.\n\n<color=" + Gold + "><b>Tips</b></color>\n" + BulletList(Tips),
                    TutorialObjective.None(), TutorialAnchors.Center, false),
            };
        }

        /// <summary>Creates a script over the default steps.</summary>
        public static TutorialScript CreateDefaultScript() => new TutorialScript(CreateDefaultSteps());

        /// <summary>Toast title for a contextual tip.</summary>
        public static string TipTitle(TutorialTipKind kind)
        {
            switch (kind)
            {
                case TutorialTipKind.FirstFamine: return "Famine!";
                case TutorialTipKind.FirstWar: return "War has begun";
                case TutorialTipKind.FirstAgeAdvance: return "A new age dawns";
                case TutorialTipKind.LowMana: return "Not enough mana";
                default: return "";
            }
        }

        /// <summary>Toast body for a contextual tip (rich text).</summary>
        public static string TipBody(TutorialTipKind kind)
        {
            switch (kind)
            {
                case TutorialTipKind.FirstFamine:
                    return "A town has run out of food. Hungry people sicken, riot and leave. " +
                           "Try <b>Rain</b> or <b>Bountiful Harvest</b> on its fields, or <b>Raise Land</b> to give it room for farms.";
                case TutorialTipKind.FirstWar:
                    return "Two peoples have taken up arms. Armies will march and towns can fall. Stay neutral - " +
                           "or tip the scales with blessings for one side and wrath for the other.";
                case TutorialTipKind.FirstAgeAdvance:
                    return "A civilization has entered a new age! New buildings and technologies are within its reach, " +
                           "and its towns will soon look different.";
                case TutorialTipKind.LowMana:
                    return "That miracle costs more <color=" + ManaHex + "><b>mana</b></color> than you have. Mana refills from " +
                           "your worshippers' <color=" + FaithHex + "><b>faith</b></color> - wait a little, or bless towns to raise devotion.";
                default: return "";
            }
        }

        private static string BulletList(string[] lines)
        {
            var sb = new System.Text.StringBuilder(512);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("• ").Append(lines[i]);
            }
            return sb.ToString();
        }
    }
}
