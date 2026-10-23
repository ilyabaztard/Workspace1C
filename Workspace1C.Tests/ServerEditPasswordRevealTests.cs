using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #49 «Просмотр пароля»: в окне «Серверы 1С» (добавить/изменить сервер)
/// кнопка просмотра пароля должна показывать пароль открытым текстом.
/// Файловые проверки исходников обеих платформ.
/// </summary>
public sealed class ServerEditPasswordRevealTests
{
    private static string ReadProjectFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, Path.Combine(parts));
        Assert.True(File.Exists(path), $"Файл проекта не найден: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Wpf_RevealField_IsPlainTextBox()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ServerEditWindow.xaml");
        Assert.Contains("<TextBox x:Name=\"PasswordRevealTextBox\"", xaml);
        Assert.DoesNotContain("<PasswordBox x:Name=\"PasswordRevealTextBox\"", xaml);
    }

    [Fact]
    public void Wpf_Toggle_CopiesPasswordToText()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ServerEditWindow.xaml.cs");
        Assert.Contains("PasswordRevealTextBox.Text = PasswordBox.Password", code);
        Assert.Contains("PasswordBox.Password = PasswordRevealTextBox.Text", code);
        Assert.DoesNotContain("PasswordRevealTextBox.Password", code);
    }

    [Fact]
    public void Avalonia_Toggle_RevealsPassword()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ServerEditWindow.Avalonia.cs");
        Assert.Contains("_passwordBox.RevealPassword = revealed", code);
    }
}
