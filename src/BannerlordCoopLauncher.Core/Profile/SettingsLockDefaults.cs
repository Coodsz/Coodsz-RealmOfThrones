using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordCoopLauncher.Core.Profile;

/// <summary>
/// The MCM values COOP Server Settings Lock used to force, as smart defaults for the host now that the mod left
/// the setup (RetiredSettingsLock). The host owns these settings: the Server Tool only adds a value its MCM file
/// does not have yet (or writes the file when there is none), and never changes a value already there. The
/// settings sync then sends the host's values to the players. Shoulder camera settings are left out, since every
/// player keeps their own camera. BetterPikes is left out too: its MCM file name could not be checked.
/// Names and types checked against XorberaxLegacy, Bannerlord.Diplomacy 1.4.8, Settlement Culture Drift and
/// Agriculture Estate on 2026-10-29; Diplomacy 1.4.8 no longer has FactionTendency or
/// EnableWarExhaustionCampaignMapWidget, and Culture Drift's ResistanceDays is a whole number.
/// </summary>
public static class SettingsLockDefaults
{
    public sealed record SeedFile(string ModuleId, string Folder, string SettingsId, IReadOnlyDictionary<string, object> Values)
    {
        /// <summary>Where MCM keeps this mod's Global settings: ModSettings\Global\FolderName\Id.json.</summary>
        public string PathIn(string modSettingsRoot) => Path.Combine(modSettingsRoot, "Global", Folder, SettingsId + ".json");
    }

    public static readonly IReadOnlyList<SeedFile> Files =
    [
        // Banks, Honest Work, Training Field and Scholars stay off: Coop does not carry their menus to the server.
        new("XorberaxLegacy", "XorberaxLegacy", "XorberaxLegacy", new Dictionary<string, object>
        {
            ["MaxSkillValue"] = 275, ["MaximumHoursToTrain"] = 24, ["TrainingCooldownHours"] = 72,
            ["TrainingCostPerUnit"] = 50, ["AdditionalFeeForSettlementFactor"] = 0.05,
            ["AdditionalExperiencePerHourPerSettlementMilitia"] = 0.1, ["EnableTrainingInjury"] = true,
            ["EnableSkillBasedExperienceGain"] = true, ["EnableTrainingFieldMod"] = false,
            ["ArenaExperiencePerHour"] = 50, ["ArenaInjuryProbability"] = 0.02, ["TrainingFieldExperiencePerHour"] = 10,
            ["TrainingFieldInjuyryProbablility"] = 0.2, ["NoviceScholarExperienceGain"] = 100,
            ["NoviceScholarCostPerAttendee"] = 2500, ["NoviceLectureDurationInHours"] = 5,
            ["IntermediateScholarExperienceGain"] = 250, ["IntermediateScholarCostPerAttendee"] = 1000,
            ["IntermediateLectureDurationInHours"] = 4, ["AdvancedScholarExperienceGain"] = 500,
            ["EnableScholarsofCalradiaMod"] = false, ["AdvancedScholarCostPerAttendee"] = 3000,
            ["AdvancedLectureDurationInHours"] = 3, ["ExpertScholarExperienceGain"] = 1000,
            ["ExpertScholarCostPerAttendee"] = 5000, ["ExpertLectureDurationInHours"] = 2,
            ["ScholarAppearanceProbability"] = 0.25, ["HoursInShift"] = 8, ["WorkCooldownInHours"] = 24,
            ["MaxPercentageOfSettlementFundsToEarnFromFullShift"] = 0.02,
            ["RelationChangeWithVillageNotablesFromFullShift"] = 1, ["AthleticsExperienceGainedFromFullShift"] = 50,
            ["EnableHonestWorkMod"] = false, ["BonusPercentageOfPaymentGainedPerWorker"] = 0.015, ["PaymentLimit"] = 500,
            ["ProbabilityOfReceivingGift"] = 0.1, ["MaxGiftQuantity"] = 15, ["EnableFriendlyFireRangedOnly"] = false,
            ["EnableFriendlyFireBattles"] = false, ["EnableFriendlyFireHideoutDuels"] = false,
            ["EnableFriendlyFireTournaments"] = false, ["EnableFriendlyFireMod"] = false,
            ["EnableFriendlyFireTowns"] = false, ["ChargeDamageMultiplier"] = 4.0, ["EnableDeadlyHorseChargeMod"] = true,
            ["PercentageOfDamageRequiredToKillUnit"] = 0.65, ["UnitHealthPercentageToCauseBleedout"] = 0.35,
            ["EnableDeadlyCombatMod"] = false, ["UnitSpeedReductionRateDuringBleedout"] = 0.45,
            ["BleedRateOverride"] = false, ["BleedRate"] = 10.0, ["EnableAICutThrough"] = false,
            ["EnableFriendlyUnitsBlockCutThrough"] = false, ["EnableCutThroughEveryoneMod"] = true,
            ["EnableOnlyKilledUnitsCutThrough"] = false, ["DamageRetainedPerCut"] = 0.79833335,
            ["PercentageOfInflictedDamageRequiredToCutThroughArmor"] = 0.5233333, ["EnableBanksofCalradiaMod"] = false,
            ["MinimumRenownRequiredToTakeOutLoan"] = 5, ["AvailableLoanAmountPerRenown"] = 200,
            ["MinimumLoanAmount"] = 100, ["RenownCostPerLoanAmountDivisor"] = 1, ["LoanTerm"] = 7,
            ["RenownLossForUnpaidLoan"] = 20, ["CrimeRatingIncreaseForUnpaidLoan"] = 10,
            ["RecurringCrimeRatingIncreaseForUnpaidLoan"] = 20,
            ["LoanLateFeeInterestRatePerSettlementProsperityFactor"] = 0.15,
            ["InterestRatePerSettlementProsperityFactor"] = 0.0875, ["InterestAccrualRateInDays"] = 7,
            ["BankAccountOpeningCostBase"] = 3000, ["BankAccountOpeningCostSettlementProsperityFactor"] = 0.01,
            ["MaxCriminalRatingForUnpaidLoan"] = 50, ["MaxBankBalance"] = 100000000
        }),
        new("Bannerlord.Diplomacy", "Diplomacy", "DiplomacySettings_v1.2", new Dictionary<string, object>
        {
            ["DailyChanceToStartRebelFaction"] = 0.05, ["DailyChanceToJoinRebelFaction"] = 0.1,
            ["DailyChanceToStartCivilWar"] = 0.1, ["MinimumTimeSinceLastCivilWarInDays"] = 240,
            ["MaximumFactionDurationInDays"] = 120, ["FactionCreationInfluenceCost"] = 100,
            ["GrantFiefPositiveRelationMultiplier"] = 1.0, ["GrantFiefRelationPenalty"] = -2,
            ["ExpansionismDecayPerDay"] = 1, ["ExpanisonismPerSiege"] = 20, ["MinimumExpansionismPerFief"] = 3,
            ["EnableMessengerAccidents"] = false, ["EnableMessengerRestictions"] = true, ["MessengerTravelTime"] = 1,
            ["SendMessengerMinimumGoldCost"] = 75, ["SendMessengerGoldHourlyCost"] = 1, ["EnableWarExhaustion"] = true,
            ["EnableDiminishingReturns"] = true, ["IndividualWarExhaustionRates"] = true,
            ["EnableFiefRepatriation"] = true, ["WarExhaustionPerDay"] = 0.25, ["FieflessWarExhaustionMultiplier"] = 10.0,
            ["WarExhaustionPerCasualty"] = 0.02, ["WarExhaustionPerCaravanRaid"] = 2.0,
            ["WarExhaustionPerImprisonment"] = 1.0, ["WarExhaustionPerDeath"] = 5.0, ["WarExhaustionPerRaid"] = 3.0,
            ["WarExhaustionPerSiege"] = 10.0, ["WarExhaustionWhenOccupied"] = 15.0, ["MaxShownBreakdownEntries"] = 35,
            ["EnableWarExhaustionDebugMessages"] = false, ["MaximumInfluenceLoss"] = 20,
            ["EnableInfluenceBalancing"] = true, ["EnableCorruption"] = false, ["InfluenceDecayPercentage"] = 1.3271605,
            ["InfluenceDecayThreshold"] = 1000, ["EnableInfluenceDecay"] = false, ["EnableStorylineProtection"] = true,
            ["EnableFiefFirstRight"] = true, ["EnableKingdomElimination"] = true, ["MinimumWarDurationInDays"] = 21,
            ["DeclareWarCooldownInDays"] = 21, ["EnableAlliances"] = true, ["AllianceTendency"] = 0,
            ["MinimumAllianceDuration"] = 42, ["LeaderClanMarriageAffectsAlliance"] = true,
            ["NonAggressionPactDuration"] = 84, ["NonAggressionPactTendency"] = 0, ["PlayerDiplomacyControl"] = false,
            ["NoWarBetweenFriends"] = true, ["NoWarOnGoodRelations"] = true, ["NoWarOnGoodRelationsThreshold"] = 30,
            ["NoWarWhenMarriedLeaderClans"] = true, ["EnableInfluenceCostsForDiplomacyActions"] = true,
            ["ScalingInfluenceCosts"] = true, ["ScalingInfluenceCostMultiplier"] = 5.0, ["DeclareWarInfluenceCost"] = 500,
            ["MakePeaceInfluenceCost"] = 500, ["ScalingGoldCosts"] = true, ["ScalingGoldCostMultiplier"] = 5.0,
            ["ScalingWarReparationsGoldCostMultiplier"] = 50.0, ["DefeatedGoldCost"] = 200
        }),
        new("SettlementCultureDrift", "SettlementCultureDrift", "SettlementCultureDrift_v1", new Dictionary<string, object>
        {
            ["FullSwitchDays"] = 84, ["RealmFactorStart"] = 0.35, ["RealmFactorFull"] = 1.5, ["ResistanceDays"] = 25,
            ["AlienSlowdown"] = 0.4, ["TownWeight"] = 3.0, ["CastleWeight"] = 2.0, ["VillageWeight"] = 1.0,
            ["SelfInertiaFactor"] = 2.0, ["HomelandFactor"] = 2.0, ["TradeLinkFactor"] = 1.0, ["GovernorWeight"] = 2.0,
            ["GovernorAcceptanceDaysFamiliar"] = 42, ["GovernorAcceptanceDaysAlien"] = 168, ["NotableFollowShare"] = 0.4,
            ["NotableFollowStep"] = 0.1
        }),
        new("AgricultureEstate", "AgricultureEstate", "AgricultureEstate", new Dictionary<string, object>
        {
            ["PlotBuyPrice"] = 920, ["PlotSellPrice"] = 200, ["UndevelopedPlotBuyPrice"] = 460,
            ["UndevelopedPlotSellPrice"] = 200, ["ProjectCost"] = 23000, ["ProjectTime"] = 10, ["LandRentScale"] = 1.0,
            ["SlaveProductionScale"] = 1.0, ["SlaveDeclineModifier"] = 1.0
        })
    ];

    /// <summary>
    /// Adds the defaults to the MCM files under <paramref name="modSettingsRoot"/> for the mods this setup turns on.
    /// A value the file already has is the host's and stays; a file that can not be read is left alone. With
    /// <paramref name="liveRoot"/>, a value only the live file (the one the server used last) has is taken from
    /// there first, so a default never goes over a value the host set on the live server. Returns the module ids
    /// whose file got defaults, the files that could not be written and the ones that could not be read (their
    /// path under the root). Start must not copy either kind over the live files: a file not written lacks the
    /// values only the live file has, and one that can not be read would replace a good live copy.
    /// </summary>
    public static SeedResult Seed(string modSettingsRoot, IEnumerable<string> enabledModuleIds, string? liveRoot = null)
    {
        var enabled = new HashSet<string>(enabledModuleIds, StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var unwritten = new List<string>();
        var unreadable = new List<string>();
        foreach (var file in Files)
        {
            if (!enabled.Contains(file.ModuleId))
                continue;
            var path = file.PathIn(modSettingsRoot);
            var settings = File.Exists(path) ? Read(path) : new JsonObject();
            if (settings == null)
            {
                unreadable.Add(Path.GetRelativePath(modSettingsRoot, path));
                continue;
            }
            // MCM reads the names without minding case, so a name in any case is the host's own value.
            var existing = new HashSet<string>(settings.Select(pair => pair.Key), StringComparer.OrdinalIgnoreCase);

            var fromLive = 0;
            var live = liveRoot == null ? null : file.PathIn(liveRoot);
            if (live != null && File.Exists(live) && Read(live) is { } liveSettings)
            {
                foreach (var (name, value) in liveSettings.ToList())
                {
                    if (!existing.Add(name))
                        continue;
                    settings[name] = value?.DeepClone();
                    fromLive++;
                }
            }

            var seeded = 0;
            foreach (var (name, value) in file.Values)
            {
                if (!existing.Add(name))
                    continue;
                settings[name] = value switch
                {
                    bool flag => JsonValue.Create(flag),
                    int whole => JsonValue.Create(whole),
                    _ => JsonValue.Create(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
                };
                seeded++;
            }
            if (seeded + fromLive == 0)
                continue;
            // Written next to it first and then moved over it, so a crash can not leave the host's file half written.
            var temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temp, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, overwrite: true);
                if (seeded > 0)
                    changed.Add(file.ModuleId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unwritten.Add(Path.GetRelativePath(modSettingsRoot, path));
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return new SeedResult(changed, unwritten, unreadable);
    }

    /// <summary>What <see cref="Seed"/> did: the module ids that got defaults, the files it could not write and the ones it could not read.</summary>
    public sealed record SeedResult(IReadOnlyList<string> Seeded, IReadOnlyList<string> Unwritten, IReadOnlyList<string> Unreadable)
    {
        /// <summary>The files Start leaves as the live server has them.</summary>
        public IReadOnlyList<string> KeepLive => Unwritten.Concat(Unreadable).ToList();
    }

    // The file as a JSON object, or null when it can not be read. Reading every name also finds a name written
    // twice, which JsonObject refuses with ArgumentException.
    private static JsonObject? Read(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject read)
                return null;
            foreach (var _ in read)
            {
            }
            return read;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
