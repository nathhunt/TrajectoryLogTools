using System;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class GoldenInputsTests
    {
        /// <summary>
        /// Writes the simulated logs for reference/golden/regenerate.sh when TLT_GOLDEN_INPUTS names
        /// a folder; otherwise checks the logs can be built.
        /// </summary>
        [Fact]
        public void WriteGoldenInputs()
        {
            string folder = Environment.GetEnvironmentVariable("TLT_GOLDEN_INPUTS");
            if (!string.IsNullOrEmpty(folder)) GoldenCases.WriteInputs(folder);
            foreach (string n in GoldenCases.Names) Assert.NotEmpty(GoldenCases.Bin(n));
        }
    }
}
