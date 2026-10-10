using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DocMe.AvatarDoctor.Editor
{
    public class SegmentedChoiceTests
    {
        [Test]
        public void SegmentedChoiceRaisesChangedOnce()
        {
            var choice = new SegmentedChoice(new List<string> { "A", "B", "C" });
            int calls = 0;
            int lastIndex = -1;
            choice.Changed += i => { calls++; lastIndex = i; };

            choice.Pick(1);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(lastIndex, Is.EqualTo(1));
            Assert.That(choice.Index, Is.EqualTo(1));
            Assert.That(choice.Items[1].ClassListContains("ad-seg-on"), Is.True);
            Assert.That(choice.Items[0].ClassListContains("ad-seg-on"), Is.False);

            // Picking the item that is already chosen does nothing.
            choice.Pick(1);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void SegmentedChoiceSetWithoutNotifyIsQuiet()
        {
            var choice = new SegmentedChoice(new List<string> { "A", "B" });
            int calls = 0;
            choice.Changed += _ => calls++;

            choice.SetIndexWithoutNotify(1);

            Assert.That(calls, Is.EqualTo(0));
            Assert.That(choice.Index, Is.EqualTo(1));
            Assert.That(choice.Items.Select(b => b.ClassListContains("ad-seg-on")), Is.EqualTo(new[] { false, true }));
        }
    }
}
