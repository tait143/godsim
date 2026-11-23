using System.Text;

namespace GodSim.Core.Races
{
    /// <summary>
    /// Long-form, original lore for the five races: a one-paragraph summary (used as
    /// <see cref="RaceDefinition.Description"/>), a 2-3 paragraph backstory, a creation myth, core
    /// values and relations with the other races, plus the name each race gives the player-god.
    /// All text is plain ASCII so it renders with any UI font. Paragraphs are separated by a blank
    /// line ("\n\n"). Lookups never throw: an unknown <see cref="RaceId"/> yields an empty string.
    /// </summary>
    public static class RaceLore
    {
        /// <summary>Separator between paragraphs in every multi-paragraph lore string.</summary>
        public const string ParagraphBreak = "\n\n";

        // Every table below is indexed by (int)RaceId: Human, Elf, Dwarf, Orc, Saurian.

        private static readonly string[] GodNames =
        {
            "the Lantern Above",
            "the Dreaming Root",
            "the Anvil-Heart",
            "the Thunder-Hunter",
            "the Sun that Remembers",
        };

        private static readonly string[] Summaries =
        {
            // Human
            "Short-lived, adaptable and endlessly fertile, Humans spread across grassland, forest and hill country, " +
            "raising timber halls that become stone towns, brick cities and at last towers of glass. Balanced in all " +
            "things, they excel at trade and at turning strangers into partners.",
            // Elf
            "Ancient, contemplative and slow to multiply, Elves dwell in deep forest and jungle canopy, weaving living " +
            "wood into platforms and leaf-domed halls among the branches. They are gifted scholars, devout and rarely " +
            "sick, but wary of outsiders and quick to close their borders.",
            // Dwarf
            "Stubborn, tireless and fiercely loyal to clan, Dwarves carve their halls into hills and mountains, " +
            "half-sunk beneath flat roofs of stone. Unmatched miners, smiths and masons, they build slowly and defend " +
            "what they build without mercy.",
            // Orc
            "Hardy, short-lived and astonishingly fertile, Orcs roam savanna, tundra, desert and broken hills in " +
            "sprawling clan-camps of hide and timber under spiked cone roofs. Fearsome warriors and relentless " +
            "expanders, they live fast, fight often and are never fewer for long.",
            // Saurian
            "Patient, pious and cold-blooded, Saurians thrive in hot swamps, jungles and sunlit beaches, raising " +
            "stilted villages over the water and stepped pyramids to the sun. Skilled fishers and devout astronomers, " +
            "they lay large clutches and outlast their enemies rather than outfight them.",
        };

        private static readonly string[] Backstories =
        {
            // Human
            "Humans remember no golden age. Their oldest songs begin in the long grass, with scattered bands following " +
            "the herds across open plains and sheltering under the eaves of the forest when storms rolled in. What set " +
            "them apart was never strength or wisdom but restlessness: a Human band rarely stayed in one valley for " +
            "more than a few generations before some of its children walked over the next hill to see what lay beyond." +
            ParagraphBreak +
            "The first true Human towns grew at river fords and crossroads, where bands met to barter salt for flint " +
            "and grain for hides. From those markets came the habits that still define them: written tallies, oaths " +
            "sworn before witnesses, and the conviction that a neighbor worth trading with is a neighbor not worth " +
            "killing. Their builders learned from everyone, borrowing Dwarven masonry and Elven joinery, and their " +
            "gabled roofs spread wherever their caravans went, timber giving way to stone, stone to brick and brick " +
            "at last to glass." +
            ParagraphBreak +
            "Humans live barely seventy years, and they know it. That brevity makes them impatient, ambitious and " +
            "prolific. Where an Elf might spend a century perfecting a single grove, a Human kingdom will rise, " +
            "quarrel, reform and rebuild itself three times over. The elder races call them a candle that burns at " +
            "both ends; Humans reply that a candle is meant for lighting the way.",

            // Elf
            "Elves count their history in the lifetimes of trees. Each Elf lives some three and a half centuries, and " +
            "their oldest groves hold rings that remember the first rains. They did not so much settle the forests as " +
            "grow up with them: the earliest Elven homes were hollows in living trunks, coaxed wider over generations " +
            "by song and patient pruning, until whole villages hung among the branches like lanterns in a net." +
            ParagraphBreak +
            "Long life made them scholars. An Elven sage may study a single question for a hundred years, and their " +
            "archives, cut in seed-script on strips of bark, record the slow turning of the stars, the moods of the " +
            "weather and every slight ever offered to them by a younger people. Because children are rare, perhaps " +
            "three in a long lifetime, every Elf is treasured and every death is mourned by the whole grove. That " +
            "scarcity makes them cautious: they will not spend lives on war when patience, a closed border or a " +
            "well-placed thicket will do." +
            ParagraphBreak +
            "The Elves are not without shame. In the age they call the Withering, a schism between the canopy houses " +
            "and the river houses nearly emptied the great forests, and the scars of that quarrel still shape their " +
            "politics: councils of many voices, endless deliberation and a deep suspicion of any single ruler. " +
            "Outsiders see arrogance. The Elves see the memory of how close they came to ruin.",

            // Dwarf
            "Dwarves were born under stone and have never fully left it. Their oldest holds began as natural caverns " +
            "in the hill country, widened chamber by chamber over generations until whole towns lay beneath the " +
            "slopes, lit by forge-glow and breathing through shafts older than any surface kingdom. A Dwarf lives " +
            "about two centuries and spends most of it working, for in Dwarven eyes an idle hand insults the " +
            "ancestors who carved the hall it rests in." +
            ParagraphBreak +
            "Dwarven history is a ledger of oaths and grudges. Every clan keeps a carved record of debts owed and " +
            "debts repaid, and the great feuds of their past were settled not by conquest but by labor: a clan that " +
            "wronged another would build it a hall, a bridge or a forge in recompense. That habit made them the finest " +
            "builders in the world and the most exacting creditors. It also made them slow to forgive, and a Dwarven " +
            "grudge can outlive the kingdom that earned it." +
            ParagraphBreak +
            "Few children are born to the Dwarves, so their holds grow deep rather than wide. They expand only when a " +
            "vein runs dry or a clan grows too proud to share a gate with its cousins. When they do march, it is " +
            "behind shield walls and siege engines rather than in raids, and a Dwarven fortress, once finished, has " +
            "rarely been taken by anyone but its own builders.",

            // Orc
            "Orcs grew up on the hard edges of the world: dry savanna, wind-scoured tundra and stony hills where " +
            "softer peoples would not stay. A single season of drought could kill half a clan, so Orcs learned to " +
            "breed quickly, come of age in a dozen years and travel light. An Orc may live only fifty years, but in " +
            "that time one matriarch can watch five generations of descendants raise banners of their own." +
            ParagraphBreak +
            "Their society is built around the clan-camp, a loose ring of hide lodges around a war-totem, moved " +
            "whenever the herds or the hunting thin out. Strength matters, but not only the strength of the arm; the " +
            "most respected Orcs are the ones who keep their clan fed through a hard winter. Raiding began as a way " +
            "to survive lean years and became a way to earn a name, and no chief holds a clan for long without " +
            "leading it to some victory, whether over a rival clan, a neighboring kingdom or the land itself." +
            ParagraphBreak +
            "The other races remember Orcs as burners and raiders, and the Orcs do not entirely disagree. Yet they " +
            "also sing of the Long Peace of the Tusk-Moot, when a dozen clans settled their feuds and raised the " +
            "first permanent Orc towns, with forges, markets and great idols to the sky. Whether such a peace can " +
            "come again depends, the elders say, on whether the clans can find enough room, and room is the one " +
            "thing the world never seems to have.",

            // Saurian
            "Saurians hatched in the warm mud of the southern mires, where the water is shallow, the sun is hot and " +
            "food swims past every doorstep. Their earliest villages stood on stilts above the flood line, linked by " +
            "rope bridges and reed causeways, and most still do. A Saurian comes of age in about ten years and lives " +
            "about ninety, and a healthy pair may raise clutch after clutch, so their villages fill quickly whenever " +
            "the fishing is good." +
            ParagraphBreak +
            "The rhythm of Saurian life follows the sun. They are sluggish in the cold and tireless in the heat, and " +
            "their priests learned long ago to track the seasons by the angle of light on carved stone. Those " +
            "observations grew into calendars, the calendars into temples and the temples into the stepped pyramids " +
            "that crown every great Saurian city. Their astronomy is ancient and precise, and other peoples are often " +
            "startled to learn that the swamp-dwellers predicted eclipses centuries before their own scholars could." +
            ParagraphBreak +
            "Saurians are not conquerors by temperament. When threatened, they withdraw into the mire, wait, and let " +
            "fever, heat and time wear their enemies down; their history is full of armies that marched into the " +
            "swamps and were never seen again. But patience is not passivity. Once a priesthood decides that the sun " +
            "demands something, be it a temple, a canal or a war, the whole clutch-city will work toward it for " +
            "generations without wavering.",
        };

        private static readonly string[] CreationMyths =
        {
            // Human
            "In the beginning the world was a dark field, and the god walked it carrying a lantern. Wherever the light " +
            "fell, grass sprang up; wherever it lingered, the first people woke and rubbed their eyes. The god could " +
            "not stay, for the field was wide, so it hung the lantern in the sky as the sun and told the people: carry " +
            "your own lights now. Humans say every hearth is a spark of that lantern and every new town a promise " +
            "that the field will not go dark again. Their Grand Cathedrals are raised tall so that their lamps can be " +
            "seen from very far away.",
            // Elf
            "Before the sky there was a seed, and the god dreamed inside it. The dream grew roots that became the " +
            "rivers and a trunk that became the pillar of the world, and from its first leaves fell the Elves, each " +
            "one a thought the god had not finished thinking. That is why Elves study: to understand the world is to " +
            "help the god finish the dream. When a World Tree is raised in an Elven city, they say the god turns " +
            "over in its sleep and smiles.",
            // Dwarf
            "The Dwarves say the world was once molten, and the god worked it as a smith works iron, hammering " +
            "mountains up out of the glowing deep. Each blow threw off sparks, and the sparks that cooled in the " +
            "stone became the first Dwarves, already holding tools. Then the god set down the hammer and slept, " +
            "leaving the world unfinished on purpose so that its children would always have work to do. The heat " +
            "beneath the mountains is the anvil still warm, and a Deep Forge built over a hot vein is a prayer that " +
            "the god will one day take up the hammer again.",
            // Orc
            "The Orcs say the god is a hunter who runs across the sky, and thunder is the sound of the chase. When " +
            "the world was young, the god wounded a great beast made of storm, and where its blood fell on the dry " +
            "earth the first Orcs rose up, hungry and shouting. The god laughed and told them: the world is a hunt, " +
            "and only the bold eat. Every War Idol is raised to show the god which clans are still running.",
            // Saurian
            "In the beginning the god was a great egg of light floating above a sea of warm mud. When the egg " +
            "cracked, the yolk rose to become the sun and the shell fell into the mire, and from each shard hatched " +
            "a Saurian. The sun still watches its children and remembers every one of them; when a Saurian dies, the " +
            "priests say, it returns to the shell to be warmed again. A Sun Pyramid is a stair for souls to climb, " +
            "each step one generation closer to the light.",
        };

        private static readonly string[] ValueTexts =
        {
            // Human
            "Humans prize enterprise, kinship and the given word. A fair bargain is sacred and a broken contract is a " +
            "scandal that can topple a dynasty. They honor founders above conquerors and remember a ruler by the " +
            "roads, bridges and markets left behind. Family is the root of everything: surnames pass down the " +
            "generations and a town's oldest families sit on its councils. Their faith is practical rather than " +
            "mystical; temples double as granaries, and the clergy keep the calendars that tell farmers when to sow.",
            // Elf
            "Elves value harmony, memory and restraint. Nothing should be taken from the forest that the forest " +
            "cannot regrow, and nothing should be decided that could not be explained to one's grandchildren's " +
            "grandchildren. Learning is the highest calling and healing the second; warriors are respected but " +
            "pitied, for the sword is the tool of those who have run out of patience. Beauty is not decoration but " +
            "proof of care, so even a granary is shaped to the curve of the branch that holds it.",
            // Dwarf
            "Dwarves honor craft, oath and ancestry. A thing worth making is worth making to last a thousand years, " +
            "and a promise is simply a thing made with words. Clans are ranked by the quality of their work rather " +
            "than the size of their armies, and the highest title a Dwarf can earn is master of a guild-hall. They " +
            "distrust novelty but respect proof: argue for a better pump and they will ask you to build it first; " +
            "build it and the whole clan will adopt it. Gold is admired less as wealth than for its refusal to " +
            "tarnish, the closest thing to virtue a metal can have.",
            // Orc
            "Orcs value courage, endurance and loyalty to the clan. Debts are paid in deeds, insults in blood and " +
            "hospitality in meat; a guest who has eaten at an Orc fire is protected by the whole camp until they " +
            "leave. Weakness is no shame in the young or the old, but cowardice is unforgivable at any age. Orcs " +
            "revere their mothers and grandmothers, who keep the clan's lineage and choose between rival war-chiefs, " +
            "and they hold that a short life well fought is better than a long one spent hiding.",
            // Saurian
            "Saurians value patience, devotion and the continuity of the clutch. Time is long and the sun is eternal, " +
            "so there is rarely a reason to hurry. Piety is woven into daily life: fishers offer their first catch to " +
            "the temple, builders align every doorway with the solstice, and the priesthood keeps the calendars that " +
            "decide when to plant, wed, build or fight. Clutch-mates are bound for life, and a Saurian family name " +
            "names the clutch rather than the parent, so cousins hatched together are closer than siblings raised " +
            "apart.",
        };

        private static readonly string[] RelationTexts =
        {
            // Human
            "Humans get along with almost everyone, which other races find either charming or suspicious. Dwarves are " +
            "their oldest partners: Human grain feeds Dwarven halls, Dwarven tools build Human cities, and both share " +
            "a fondness for ledgers. Elves are respected but distant; Humans admire their libraries and resent their " +
            "condescension. Orcs are the great border threat, raiders on the edge of the grasslands, though some " +
            "frontier towns trade with Orc clans in quiet years. Saurians are a curiosity, patient southern " +
            "neighbors whose fish markets and sun-calendars Human merchants find surprisingly profitable.",
            // Elf
            "Elves keep the younger races at arm's length. Humans are tolerated as eager students and useful traders, " +
            "though their haste is exhausting. Dwarves are an old rivalry: the Dwarven hunger for timber and ore " +
            "strikes Elves as vandalism, and Dwarves in turn mock the Elves as tree-dwellers who never finish " +
            "anything. Orcs are the enemy remembered in a hundred laments, burners of groves and breakers of truces. " +
            "Toward the Saurians, who share the jungle edges, Elves feel the cautious kinship of one patient people " +
            "for another, sealed by rare exchanges of herb lore.",
            // Dwarf
            "Humans are the favored partners of the Dwarves: reliable customers with short memories, and therefore " +
            "easy to forgive. Elves are old rivals; generations of disputes over felled forests and flooded mines have " +
            "left mutual contempt wrapped in elaborate courtesy. Orcs are the enemy at the gate, raiders who covet " +
            "Dwarven steel and have broken the outer walls of more than one hold. Saurians are distant and mostly " +
            "irrelevant to Dwarven politics, though their stepped pyramids earn grudging professional respect from " +
            "Dwarven masons.",
            // Orc
            "Orcs regard most of the world as rivals or prey. Elves are the oldest enemy, haughty forest-folk who " +
            "hoard the best land and keep a long memory of every grove that burned. Dwarves are hated and envied in " +
            "equal measure: their fortresses are an insult, but their steel is the finest plunder. Humans are " +
            "dangerous but practical neighbors who will trade weapons for furs when the raids pause. Saurians are " +
            "strange and cold-blooded; Orcs mistrust their patience and their slow, unblinking priests, though open " +
            "war between the two is rare.",
            // Saurian
            "Saurians hold few strong feelings about other races, which other races find unsettling. Humans are " +
            "welcome trading partners and noisy, short-tempered guests. Elves are respected as fellow keepers of old " +
            "knowledge, though they rarely leave their trees long enough to visit. Dwarves are remote, admired for " +
            "their stonework and of little concern. Orcs are the one people Saurians openly distrust: their raids are " +
            "loud, frequent and careless of sacred ground, and the priesthood keeps a long count of the temples the " +
            "clans have defiled.",
        };

        /// <summary>Name the race gives the player-god (e.g. "the Lantern Above"); lowercase article for mid-sentence use.</summary>
        public static string GodName(RaceId race) => Get(GodNames, race);

        /// <summary>One-paragraph overview; also used as <see cref="RaceDefinition.Description"/>.</summary>
        public static string Summary(RaceId race) => Get(Summaries, race);

        /// <summary>Two to three paragraphs of history, separated by <see cref="ParagraphBreak"/>.</summary>
        public static string Backstory(RaceId race) => Get(Backstories, race);

        /// <summary>The race's own account of how the god made the world and them.</summary>
        public static string CreationMyth(RaceId race) => Get(CreationMyths, race);

        /// <summary>What the race honors and how its society is organized.</summary>
        public static string Values(RaceId race) => Get(ValueTexts, race);

        /// <summary>How the race regards each of the other four races.</summary>
        public static string Relations(RaceId race) => Get(RelationTexts, race);

        /// <summary>
        /// Every section of a race's lore in reading order, each under a short heading line, for
        /// encyclopedia/UI panels. Allocates; call on demand, not per tick. Unknown race: empty string.
        /// </summary>
        public static string FullText(RaceId race)
        {
            string summary = Summary(race);
            if (summary.Length == 0) return "";
            var sb = new StringBuilder(4096);
            sb.Append(summary).Append(ParagraphBreak);
            sb.Append("History").Append('\n').Append(Backstory(race)).Append(ParagraphBreak);
            sb.Append("Creation (").Append(GodName(race)).Append(')').Append('\n').Append(CreationMyth(race)).Append(ParagraphBreak);
            sb.Append("Values").Append('\n').Append(Values(race)).Append(ParagraphBreak);
            sb.Append("Relations").Append('\n').Append(Relations(race));
            return sb.ToString();
        }

        /// <summary>Counts non-empty paragraphs separated by <see cref="ParagraphBreak"/>. Null/empty: 0.</summary>
        public static int ParagraphCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0, start = 0;
            while (start <= text.Length)
            {
                int end = text.IndexOf(ParagraphBreak, start, System.StringComparison.Ordinal);
                if (end < 0) end = text.Length;
                if (HasContent(text, start, end)) count++;
                start = end + ParagraphBreak.Length;
            }
            return count;
        }

        private static bool HasContent(string s, int start, int end)
        {
            for (int i = start; i < end; i++) if (!char.IsWhiteSpace(s[i])) return true;
            return false;
        }

        private static string Get(string[] table, RaceId race)
        {
            int i = (int)race;
            return i < table.Length ? table[i] ?? "" : "";
        }
    }
}
