#if DOCME_NDMF && DOCME_VRCSDK3A
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DocMe.AvatarDoctor.Editor
{
    public class ReadmeTests
    {
        const string RawBase = "https://raw.githubusercontent.com/DocMeVR/AvatarDoc/main/";

        static string Readme() => File.ReadAllText(QuickStart.ReadmePath());

        [Test]
        public void TheReadmeIsShort()
        {
            var lines = Readme().Split('\n').Length;
            Assert.That(lines, Is.LessThanOrEqualTo(170), "The README is a front page. Detail belongs on the docs site in Website~.");
        }

        [Test]
        public void TheQuickStartSectionIsFoundAndHasNoImage()
        {
            var section = QuickStart.QuickStartSection();
            Assert.That(section, Is.Not.Null.And.Not.Empty, "Quick start reads the README's '## Quick start' section.");
            Assert.That(section, Does.Not.Contain("!["), "The Quick start window shows plain text, so the section holds no image.");
            Assert.That(section, Does.Not.Contain("<img"), "The Quick start window shows plain text, so the section holds no image.");
        }

        [Test]
        public void EveryPictureNamesAShotThatExists()
        {
            var addresses = Regex.Matches(Readme(), Regex.Escape(RawBase) + @"[^\s)""]+").Cast<Match>().Select(m => m.Value).ToList();
            Assert.That(addresses, Is.Not.Empty, "The README shows pictures from the docs site's shots.");
            var root = QuickStart.PackageRoot();
            foreach (var address in addresses)
            {
                Assert.That(address, Does.StartWith(RawBase + "Website~/src/assets/shots/").And.EndsWith(".png"), address);
                var file = Path.Combine(root, address.Substring(RawBase.Length));
                Assert.That(File.Exists(file), Is.True, "The README shows " + address + " but " + file + " does not exist.");
            }
        }

        [Test]
        public void TheReadmeLinksTheDocsSite()
        {
            Assert.That(Readme(), Does.Contain("https://docmevr.github.io/AvatarDoc/"));
        }

        [Test]
        public void TheReadmeHasNoEmDash()
        {
            Assert.That(Readme(), Does.Not.Contain("\u2014"));
        }
    }
}
#endif
