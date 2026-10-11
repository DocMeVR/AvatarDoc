#if DOCME_NDMF && DOCME_VRCSDK3A
using System.IO;
using NUnit.Framework;

namespace DocMe.AvatarDoctor.Editor
{
    public class ReleaseWorkflowTests
    {
        static string Workflow() => File.ReadAllText(Path.Combine(QuickStart.PackageRoot(), ".github/workflows/release-please.yml"));

        [TestCase("'.git*'")]
        [TestCase("'Tests'")]
        [TestCase("'Tests.meta'")]
        [TestCase("'Docs~'")]
        [TestCase("'Website~'")]
        [TestCase("'/CHANGELOG.md'")]
        [TestCase("'/CHANGELOG.md.meta'")]
        public void TheReleaseDownloadLeavesOut(string pattern)
        {
            Assert.That(Workflow(), Does.Contain("--exclude " + pattern),
                "The release workflow must keep " + pattern + " out of the package creators download. Add --exclude " + pattern + " to the rsync line of the Stage The Package step in .github/workflows/release-please.yml.");
        }

        [TestCase("--exclude '/README.md'")]
        [TestCase("--exclude 'README.md'")]
        public void TheReleaseDownloadKeepsTheReadme(string forbidden)
        {
            Assert.That(Workflow(), Does.Not.Contain(forbidden),
                "Quick start reads the README, so it must ship in the package creators download. Remove " + forbidden + " from the rsync line in .github/workflows/release-please.yml.");
        }

        [Test]
        public void AnExistingReleaseCanBeRepacked()
        {
            Assert.That(Workflow(), Does.Contain("inputs.tag"),
                "The release workflow needs a tag input, so an existing release can be repacked.");
        }
    }
}
#endif
