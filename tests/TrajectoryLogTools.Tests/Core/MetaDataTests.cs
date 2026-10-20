using Xunit;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Tests
{
    public class MetaDataTests
    {
        const string Text = "Patient ID:\tPAT001\r\nPlan Name:\tPlan1\r\n";

        [Fact]
        public void Get_And_FindKey()
        {
            Assert.Equal("PAT001", MetaData.Get(Text, "patient id"));
            Assert.Null(MetaData.Get(Text, "Plan UID"));
            Assert.Equal("Plan Name", MetaData.FindKey(Text, MetaData.PlanNameKeys));
            Assert.Null(MetaData.FindKey(Text, MetaData.PlanUidKeys));
        }

        [Fact]
        public void Set_KeepsSeparatorAndNewlines()
        {
            Assert.Equal("Patient ID:\tQA\r\nPlan Name:\tPlan1\r\n", MetaData.Set(Text, "Patient ID", "QA"));
            Assert.Equal("Key: new\nOther=1", MetaData.Set("Key: old\nOther=1", "key", "new"));
            Assert.Equal("Patient ID:\tPAT001\r\nPlan Name:\tPlan1\r\nPlan UID:\t9\r\n", MetaData.Set(Text, "Plan UID", "9"));
            Assert.Equal("Plan Name:\tPlan1\r\n", MetaData.Set(Text, "Patient ID", null));
            Assert.Equal("Patient ID:\t\r\nPlan Name:\tPlan1\r\n", MetaData.Set(Text, "Patient ID", ""));
        }

        [Fact]
        public void Set_RejectsMultiLineValue()
        {
            Assert.Throws<System.ArgumentException>(() => MetaData.Set(Text, "Patient ID", "a\nb"));
        }
    }
}
