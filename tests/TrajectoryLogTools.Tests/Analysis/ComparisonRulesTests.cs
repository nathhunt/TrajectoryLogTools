using TrajectoryLogTools.Analysis;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class ComparisonRulesTests
    {
        [Theory]
        [InlineData(0.0, 0.0, true)]
        [InlineData(0.0099, 0.0499, true)]
        [InlineData(0.01, 0.0, false)]    // the limits themselves are a difference
        [InlineData(0.0, 0.05, false)]
        [InlineData(2.0, 0.0, false)]
        public void IsSamePlan_PlannedDifferencesBelowTheLimits(double mlc, double gantry, bool same)
        {
            Assert.Equal(same, ComparisonRules.IsSamePlan(mlc, gantry));
        }

        [Fact]
        public void IsSamePlan_WhatWasNotComparedDoesNotCount_NaNDoes()
        {
            Assert.True(ComparisonRules.IsSamePlan(null, null));
            Assert.True(ComparisonRules.IsSamePlan(0.0, null));
            Assert.False(ComparisonRules.IsSamePlan(null, 1.0));
            Assert.False(ComparisonRules.IsSamePlan(double.NaN, 0.0));
        }

        [Fact]
        public void WithinTolerance()
        {
            Assert.Null(ComparisonRules.WithinTolerance(5.0, null));          // no tolerance set
            Assert.True(ComparisonRules.WithinTolerance(0.99, 1.0));
            Assert.True(ComparisonRules.WithinTolerance(1.0, 1.0));           // equal is within
            Assert.False(ComparisonRules.WithinTolerance(1.0001, 1.0));
            Assert.False(ComparisonRules.WithinTolerance(double.NaN, 1.0));   // nothing measured: not shown to be within
        }

        [Theory]
        [InlineData("0.5", 0.5)]
        [InlineData(" 2 ", 2.0)]
        [InlineData("1e-1", 0.1)]
        public void ParseTolerance_Numbers(string text, double value)
        {
            Assert.Equal(value, ComparisonRules.ParseTolerance(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("0,5")]      // invariant culture: a comma isn't a decimal point
        [InlineData("NaN")]
        [InlineData("abc")]
        public void ParseTolerance_NoneOrInvalid(string text)
        {
            Assert.Null(ComparisonRules.ParseTolerance(text));
        }
    }
}
