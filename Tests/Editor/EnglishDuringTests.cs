#if DOCME_NDMF
using System;
using NUnit.Framework;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>
    /// Unity's own Test Runner runs every test in this namespace in English, whatever language the editor shows: many
    /// tests compare English texts, which also guards those texts. The project's own runner ignores a set up fixture and
    /// pins English itself; the two scopes nest.
    /// </summary>
    [SetUpFixture]
    public class EnglishDuringTests
    {
        IDisposable english;

        [OneTimeSetUp]
        public void PinEnglish() => english = L.UseTestLanguage("en-US", null);

        [OneTimeTearDown]
        public void LetGo()
        {
            english?.Dispose();
            english = null;
        }
    }
}
#endif
