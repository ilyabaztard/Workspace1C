using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #26 «Исправить отображение сохраненных акцентных цветов и контрастность текста
/// в тёмной теме» (последний комментарий ArtyoZ от 2026-10-27): все кнопки приложения
/// приведены к единому контракту — акцентная ModernButton (заливка акцентом темы, текст
/// контрастный TextOnAccentBrush по WCAG), вторичная SecondaryButton (карточка + контур +
/// текст темы), опасная DangerButton (красная заливка, белый текст). Локальные «зелёные»
/// (#16A34A/#22C55E/#06B6D4) и «красные» (#EF4444/#DC2626) кнопки и стили запрещены.
///
/// Файловые проверки исходников (по образцу Issue61DarkPaletteAuditTests /
/// ButtonThemeBindingTests): общая логика контраста уже покрыта ThemeContrastRegressionTests,
/// здесь стражи против возврата жёстких цветов в разметку и расхождения платформ.
/// </summary>
public sealed class Issue26ButtonUniformityTests
{
    private static readonly string[] ForbiddenButtonSetters =
    {
        "Background=\"#16A34A\"", // зелёная кнопка-подтверждение (прежний вид диалогов)
        "Background=\"#22C55E\"", // зелёная кнопка «Сменить платформу»/иконки-команды
        "Background=\"#06B6D4\"", // циановая кнопка «Импортировать»/«Заменить»
        "Background=\"#DC2626\"", // красная кнопка «Удалить»
        "Background=\"#EF4444\"", // красная кнопка «Стоп»/«Очистить»
        "BorderBrush=\"#EF4444\"" // красный контур кнопки «Отмена»
    };

    private static readonly string[] AccentColorSamples =
    {
        "#FDBF00", // светлый янтарь (светлая тема)
        "#FFB300", // янтарь (тёмная тема)
        "#16A34A", // зелёный (пользовательский)
        "#22C55E", // зелёный-команда
        "#2563EB", // синий
        "#7C4DFF", // фиолетовый
        "#13522C", // тёмная зелень (жалоба 1.0.0.33)
        "#AF6B1D"  // тёмный янтарь (со скриншота issue #26)
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string[] ReadViewFiles(string pattern)
    {
        var viewsDir = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        Assert.True(Directory.Exists(viewsDir), $"Каталог Views не найден: {viewsDir}");
        return Directory.GetFiles(viewsDir, pattern)
            .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal))
            .ToArray();
    }

    // ===================== Единый контракт в темах =====================

    [Fact]
    public void WpfThemes_ModernButton_UsesTextOnAccentBrush()
    {
        // Контракт акцентной кнопки: текст считает ThemeManager по WCAG (≥ 4.5:1)
        // против фактического акцента, а не фиксированный белый/чёрный.
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains(
                "Setter Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentBrush}\"",
                xaml);
        }
    }

    [Fact]
    public void Avalonia_ModernButton_UsesTextOnAccentBrush()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        Assert.Contains(
            "Setter Property=\"Foreground\" Value=\"{DynamicResource TextOnAccentBrush}\"",
            xaml);
    }

    [Fact]
    public void WpfThemes_HaveDangerButton()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("x:Key=\"DangerButton\"", xaml);
            Assert.Contains("Foreground", xaml);
            Assert.Contains("{DynamicResource DangerBrush}", xaml);
            Assert.Contains("{DynamicResource DangerTextBrush}", xaml);
        }
    }

    [Fact]
    public void Avalonia_HasDangerButton_AndDialogsUseUnifiedContract()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        // Диалоговые пары подтверждения/отмены стали обёртками единого контракта:
        // подтверждение — акцентная ModernButton, отмена — вторичная SecondaryButton.
        Assert.Contains("x:Key=\"DialogConfirmButton\" TargetType=\"Button\" BasedOn=\"{StaticResource ModernButton}\"", xaml);
        Assert.Contains("x:Key=\"DialogCancelButton\" TargetType=\"Button\" BasedOn=\"{StaticResource SecondaryButton}\"", xaml);
        Assert.Contains("x:Key=\"DangerButton\" TargetType=\"Button\" BasedOn=\"{StaticResource ModernButton}\"", xaml);
        Assert.Contains("x:Key=\"DangerButton\"", xaml);
        // У опасной кнопки — красная заливка и белый текст (по WCAG контрастен #DC2626).
        Assert.Contains("{DynamicResource DangerBrush}", xaml);
        Assert.Contains("{DynamicResource DangerTextBrush}", xaml);
    }

    // ===================== Никаких жёстких кнопок в разметке окон =====================

    [Fact]
    public void WpfViews_HaveNoHardcodedButtonColors()
    {
        foreach (var file in ReadViewFiles("*.xaml"))
        {
            var name = Path.GetFileName(file);
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

            foreach (var setter in ForbiddenButtonSetters)
            {
                var count = Regex.Matches(text, Regex.Escape(setter)).Count;
                // В SettingsWindow.xaml осталась одна зелёная «точка»-индикатор
                // (12x12 Border, не кнопка) — единственное разрешённое вхождение #22C55E.
                if (name == "SettingsWindow.xaml" && setter == "Background=\"#22C55E\"")
                {
                    Assert.True(count <= 1,
                        $"{name}: индикатор #22C55E допускается не более одного раза, найдено {count}.");
                    continue;
                }

                Assert.True(count == 0,
                    $"{name}: найден жёсткий кнопочный цвет {setter} ({count} шт.) — " +
                    "кнопки обязаны использовать единый контракт ModernButton/SecondaryButton/DangerButton (issue #26).");
            }
        }
    }

    [Fact]
    public void WpfViews_LocalPrimaryStyles_AreBasedOnUnifiedContract()
    {
        // Локальные PrimaryButtonStyle/OutlineButtonStyle (окна справочников и диалогов)
        // — это обёртки ModernButton/SecondaryButton, а не собственные цветовые схемы.
        foreach (var file in ReadViewFiles("*.xaml"))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(text, @"x:Key=""(PrimaryButtonStyle|OutlineButtonStyle)""\s+TargetType=""Button""\s+BasedOn=""\{StaticResource (ModernButton|SecondaryButton)\}"""))
            {
                var key = m.Groups[1].Value;
                var baseOn = m.Groups[2].Value;
                var expected = key == "PrimaryButtonStyle" ? "ModernButton" : "SecondaryButton";
                Assert.Equal(expected, baseOn);
            }

            // Если стили есть — они обязаны быть приведены к контракту (не «голые»).
            if (text.Contains("x:Key=\"PrimaryButtonStyle\"", StringComparison.Ordinal))
                Assert.Contains("PrimaryButtonStyle\" TargetType=\"Button\" BasedOn=\"{StaticResource ModernButton}\"", text);
        }
    }

    // ===================== Avalonia-код без жёстких кнопок =====================

    [Fact]
    public void AvaloniaCode_HasNoGreenOrCyanButtonOverrides()
    {
        // Кнопки, собранные кодом: жёсткие переопределения фона на акцентных стилях
        // запрещены (кнопка берёт акцент темы, текст — TextOnAccentBrush).
        var banned = new (string File, string Token)[]
        {
            ("ClusterMigrationWindow.Avalonia.cs", "#16A34A"),
            ("PlatformChangeWindow.Avalonia.cs", "#22C55E"),
            ("ClusterImportWindow.Avalonia.cs", "#06B6D4"),
            ("ConnectionReplaceWindow.Avalonia.cs", "#06B6D4")
        };

        foreach (var (fileName, token) in banned)
        {
            var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));
            Assert.DoesNotContain(token, code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DangerousWindows_UseDangerButton()
    {
        // Опасные действия (удаление/остановка/очистка) переведены на DangerButton
        // в обеих платформах — семантика красной кнопки сохранена.
        var wpfList = new (string File, string Token)[]
        {
            ("DeleteInfobaseWindow.xaml", "DangerButton"),
            ("ClearHistoryWindow.xaml", "DangerButton"),
            ("DetectConfigurationsWindow.xaml", "DangerButton"),
            ("FindLostBasesWindow.xaml", "DangerButton"),
            ("PlatformOldVersionsWindow.xaml", "DangerButton"),
            ("RacInfobaseDropWindow.xaml", "DangerButton"),
            ("RacInfobaseDropRetryWindow.xaml", "DangerButton")
        };
        foreach (var (fileName, token) in wpfList)
            Assert.Contains(token,
                ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName)));

        var avaloniaList = new (string File, string Token)[]
        {
            ("RacInfobaseDropWindow.Avalonia.cs", "ControlThemes.DangerButton"),
            ("RacInfobaseDropRetryWindow.Avalonia.cs", "ControlThemes.DangerButton"),
            ("PlatformOldVersionsWindow.Avalonia.cs", "ControlThemes.DangerButton"),
            ("ClearHistoryWindow.Avalonia.cs", "ControlThemes.DangerButton"),
            ("DetectConfigurationsWindow.Avalonia.cs", "ControlThemes.DangerButton"),
            ("FindLostBasesWindow.Avalonia.cs", "ControlThemes.DangerButton")
        };
        foreach (var (fileName, token) in avaloniaList)
            Assert.Contains(token,
                ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName)));
    }

    // ===================== Контраст текста на акцентных кнопках =====================

    [Fact]
    public void TextOnAccent_ContrastMeetsWcag_ForButtonAccents()
    {
        // Акцентная кнопка любой схемы: текст TextOnAccentBrush (ContrastTextHex)
        // читается на заливке акцента — WCAG AA (≥ 4.5:1), включая тёмные акценты
        // из жалобы и зелёные/синие пользовательские схемы.
        foreach (var accent in AccentColorSamples)
        {
            var text = ColorScheme.ContrastTextHex(accent);
            Assert.True(ColorScheme.ContrastRatio(text, accent) >= 4.5,
                $"Текст {text} на акценте {accent} должен давать контраст ≥ 4.5:1 (issue #26).");
        }
    }
}