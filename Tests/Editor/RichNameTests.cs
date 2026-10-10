using NUnit.Framework;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class RichNameTests
    {
        [Test]
        public void StripRemovesEveryTag()
        {
            Assert.That(RichName.Strip("<color=#FF0000><b>Hat</b></color>"), Is.EqualTo("Hat"));
            Assert.That(RichName.Strip("Plain"), Is.EqualTo("Plain"));
            Assert.That(RichName.Strip("<size=120%>Big</size> hat"), Is.EqualTo("Big hat"));
            Assert.That(RichName.Strip(null), Is.EqualTo(""));
        }

        [Test]
        public void ComposeAndParseRoundTrip()
        {
            var composed = RichName.Compose("Hat", new Color(1f, 0f, 0f), bold: true, italic: false);
            Assert.That(composed, Is.EqualTo("<color=#FF0000><b>Hat</b></color>"));
            var style = RichName.Parse(composed);
            Assert.That(style.Plain, Is.EqualTo("Hat"));
            Assert.That(style.Color, Is.EqualTo(new Color(1f, 0f, 0f)));
            Assert.That(style.Bold, Is.True);
            Assert.That(style.Italic, Is.False);
            Assert.That(style.Custom, Is.False);
            Assert.That(RichName.Compose("Hat", null, false, false), Is.EqualTo("Hat"));
            Assert.That(RichName.Compose("Hat", null, false, true), Is.EqualTo("<i>Hat</i>"));
        }

        [Test]
        public void UnknownShapesAreCustom()
        {
            var style = RichName.Parse("Ha<b>t</b>");
            Assert.That(style.Custom, Is.True);
            Assert.That(style.Plain, Is.EqualTo("Hat"));
            Assert.That(RichName.Parse("Hat").Custom, Is.False);
        }

        [Test]
        public void ParseTreatsUnbalancedOrReorderedTagsAsCustom()
        {
            Assert.That(RichName.Parse("<b>Hat").Custom, Is.True, "an opening tag on its own");
            Assert.That(RichName.Parse("Hat</b>").Custom, Is.True, "a closing tag on its own");
            Assert.That(RichName.Parse("<i><b>Hat</b></i>").Custom, Is.True, "the wrappers are the other way round");

            var upper = RichName.Parse("<B>Hat</B>");
            Assert.That(upper.Custom, Is.False);
            Assert.That(upper.Bold, Is.True);
            Assert.That(upper.Plain, Is.EqualTo("Hat"));

            var alpha = RichName.Parse("<color=#FF0000FF>Hat</color>");
            Assert.That(alpha.Custom, Is.False);
            Assert.That(alpha.Color, Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
            Assert.That(alpha.Plain, Is.EqualTo("Hat"));
        }

        [Test]
        public void AppendKeepsTheWrappers()
        {
            Assert.That(RichName.Append("<b>Hat</b>", " Copy"), Is.EqualTo("<b>Hat Copy</b>"));
            Assert.That(RichName.Append("Hat", " Copy"), Is.EqualTo("Hat Copy"));
            Assert.That(RichName.Append("Ha<b>t</b>", " Copy"), Is.EqualTo("Ha<b>t</b> Copy"));
        }
    }
}
