using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class DialogServiceTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void CloseButtonTemplate_IsBuiltBeforeItIsApplied()
    {
        string? originalWindir = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrWhiteSpace(originalWindir))
        {
            string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(systemRoot))
                Environment.SetEnvironmentVariable("WINDIR", systemRoot);
        }

        try
        {
            var template = DialogService.CreateCloseButtonTemplate();
            var button = new Button();

            Assert.DoesNotThrow(() => button.Template = template);
            Assert.That(template.Triggers.Count, Is.EqualTo(1));
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(originalWindir))
                Environment.SetEnvironmentVariable("WINDIR", null);
        }
    }
}
