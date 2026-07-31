using PgOperator.Core.Models;
using PgOperator.AI.Models;

namespace PgOperator.Tests;

[TestClass]
public class ModelsTests
{
    // ─── BackupFileInfo ───────────────────────────────────

    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(500L, "500 B")]
    [DataRow(1024L, "1.0 KB")]
    [DataRow(1_048_576L, "1.0 MB")]
    [DataRow(1_073_741_824L, "1.0 GB")]
    [DataRow(2_147_483_648L, "2.0 GB")]
    public void BackupFileInfo_SizeFormatted_Correctly(long bytes, string expected)
    {
        var info = new BackupFileInfo { SizeBytes = bytes };
        Assert.AreEqual(expected, info.SizeFormatted);
    }

    [TestMethod]
    [DataRow("mydb.dump", true, false)]
    [DataRow("mydb.sql", true, false)]
    [DataRow("mydb.sql.gz", true, false)]
    [DataRow("basebackup_20260731", false, true)]
    [DataRow("basebackup_20260731-120000", false, true)]
    [DataRow("unknown_file.txt", false, false)]
    public void BackupFileInfo_IsLogical_IsPhysical(string fileName, bool isLogical, bool isPhysical)
    {
        var info = new BackupFileInfo { FileName = fileName };
        Assert.AreEqual(isLogical, info.IsLogical, $"IsLogical mismatch for: {fileName}");
        Assert.AreEqual(isPhysical, info.IsPhysical, $"IsPhysical mismatch for: {fileName}");
    }

    // ─── DiskSpaceCheckResult ─────────────────────────────

    [TestMethod]
    [DataRow(85, 100, 50, true)]   // high usage → warn
    [DataRow(50, 100, 50, false)]  // normal usage, enough space
    [DataRow(50, 100, 200, true)]  // low available vs required → warn
    public void DiskSpaceCheckResult_ShouldWarn(int usagePct, double availableMb, double requiredMb, bool expected)
    {
        var result = new DiskSpaceCheckResult
        {
            UsagePercent = usagePct,
            AvailableMb = availableMb,
            RequiredMb = requiredMb
        };
        Assert.AreEqual(expected, result.ShouldWarn);
    }

    [TestMethod]
    public void DiskSpaceCheckResult_Defaults_CanProceedFalse()
    {
        var result = new DiskSpaceCheckResult();
        Assert.IsFalse(result.CanProceed);
        Assert.AreEqual(0, result.AvailableMb);
        Assert.AreEqual("", result.Reason);
    }

    // ─── BatchDeleteResult ─────────────────────────────────

    [TestMethod]
    [DataRow(500L, "0.5 KB", DisplayName = "sub-KB")]
    [DataRow(1_048_576L, "1.0 MB", DisplayName = "MB")]
    [DataRow(1_073_741_824L, "1.0 GB", DisplayName = "GB")]
    public void BatchDeleteResult_FreedFormatted(long freedBytes, string expected)
    {
        var result = new BatchDeleteResult { FreedBytes = freedBytes };
        Assert.AreEqual(expected, result.FreedFormatted);
    }

    // ─── AiAnalysisResult ─────────────────────────────────

    [TestMethod]
    public void AiAnalysisResult_Success_NoError()
    {
        var result = new AiAnalysisResult { Summary = "OK" };
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public void AiAnalysisResult_Success_WithError()
    {
        var result = new AiAnalysisResult { Error = "Something went wrong" };
        Assert.IsFalse(result.Success);
    }

    // ─── AiRecommendation ──────────────────────────────────

    [TestMethod]
    public void AiRecommendation_ActionStepsStr_JoinsWithDollar()
    {
        var rec = new AiRecommendation
        {
            ActionSteps = new List<string> { "step1", "step2", "step3" }
        };
        var str = rec.ActionStepsStr;
        StringAssert.Contains(str, "$ step1");
        StringAssert.Contains(str, "$ step2");
        StringAssert.Contains(str, "$ step3");
    }

    [TestMethod]
    public void AiRecommendation_ActionStepsStr_Empty()
    {
        var rec = new AiRecommendation();
        Assert.AreEqual("", rec.ActionStepsStr);
    }

    // ─── AiConfig defaults ─────────────────────────────────

    [TestMethod]
    public void AiConfig_Defaults()
    {
        var config = new AiConfig();
        Assert.AreEqual("deepseek", config.Provider);
        Assert.AreEqual("balanced", config.Preference);
        Assert.AreEqual("performance", config.Focus);
    }

    // ─── BackupJob defaults ────────────────────────────────

    [TestMethod]
    public void BackupJob_Defaults()
    {
        var job = new BackupJob();
        Assert.AreNotEqual(Guid.Empty, job.Id);
        Assert.AreEqual(BackupType.Logical, job.Type);
        Assert.AreEqual(BackupFormat.Custom, job.Format);
        Assert.AreEqual("/var/backups/postgresql", job.RemotePath);
        Assert.AreEqual(7, job.RetentionDays);
        Assert.IsTrue(job.IsEnabled);
    }
}
