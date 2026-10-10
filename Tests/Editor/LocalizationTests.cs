using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;

namespace DocMe.AvatarDoctor.Editor
{
    public class LocalizationTests
    {
        [Test]
        public void EveryMessageKeyHasEnglishText()
        {
            foreach (var key in L.KnownKeys.Concat(L.UiKeys))
                Assert.That(L.Localizer.TryGetLocalizedString(key, out _), Is.True, key);
        }

        [Test]
        public void ReportMapsSeveritiesAndAttachesObjects()
        {
            var go = new GameObject("Ctx");
            try
            {
                var errors = ErrorReport.CaptureErrors(() =>
                {
                    L.Report(ModelMessage.Info("ad.info.menu_split", "Root"));
                    L.Report(ModelMessage.Warning("ad.warn.no_effects", "Hat", go));
                    L.Report(ModelMessage.Error("ad.error.bits_exceeded", "300", "256", "44"));
                });
                Assert.That(errors.Count, Is.EqualTo(3));
                Assert.That(errors.Select(e => e.TheError.Severity), Is.EqualTo(new[] { ErrorSeverity.Information, ErrorSeverity.NonFatal, ErrorSeverity.Error }));
                Assert.That(errors[1].TheError.ToMessage(), Does.Contain("Hat"));
                Assert.That(errors[2].TheError.ToMessage(), Does.Contain("300"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
