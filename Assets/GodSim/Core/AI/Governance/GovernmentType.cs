namespace GodSim.Core.AI
{
    /// <summary>
    /// Forms of government a civilization can adopt. Stored as a float index in civ memory key "gov.type".
    /// Do not reorder (saves persist the numeric value); append new forms at the end.
    /// </summary>
    public enum GovernmentType : byte
    {
        TribalCouncil = 0,
        Chiefdom = 1,
        Theocracy = 2,
        Monarchy = 3,
        FeudalRealm = 4,
        MerchantRepublic = 5,
        Empire = 6,
        ConstitutionalState = 7,
        Technocracy = 8,
        AscendantCommonwealth = 9
    }
}
