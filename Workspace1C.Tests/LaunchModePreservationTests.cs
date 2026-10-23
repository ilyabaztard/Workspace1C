using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты сохранности режима запуска (LaunchMode) при импорте ibases.v8i (issue #59):
/// импорт не должен затирать «Толстый клиент», заданный в приложении, когда в файле
/// нет явного режима (App/DefaultApp = Auto), и должен распознавать токены штатного
/// стартера 1С («1CV8»/«1cv8c»/«1cv8s»).
/// </summary>
public sealed class LaunchModePreservationTests
{
    [Fact]
    public void Import_DoesNotOverwriteLaunchMode_WhenFileHasNoExplicitMode()
    {
        // Регресс issue #59: база с «Толстый клиент» в приложении; в ibases.v8i
        // режим не задан (App=Auto, DefaultApp=Auto — штатное значение стартера).
        // Стартовая синхронизация НЕ должна вернуть «Автоматический».
        var filePath = WriteIbases("""
            [База]
            ID=base-guid
            Folder=/
            Connect=Srvr="srv-db";Ref="База";
            App=Auto
            DefaultApp=Auto

            """);

        var infobases = new List<Infobase>
        {
            new()
            {
                Name = "База",
                Id = "base-guid",
                LaunchMode = "Толстый клиент",
                Connection = new ConnectionSettings
                {
                    Type = ConnectionType.ClientServer,
                    Server = "srv-app",
                    DatabaseName = "База"
                }
            }
        };

        IbasesV8iImporter.Import(filePath, infobases, new List<Group>());

        var base1 = Assert.Single(infobases);
        Assert.Equal("Толстый клиент", base1.LaunchMode);
    }

    [Fact]
    public void Import_AppliesExplicitLaunchMode_FromRealStarterToken()
    {
        // В файле явный режим штатного стартера («1CV8» — толстый клиент):
        // он применяется и к существующей базе (по ID), и к новой.
        var filePath = WriteIbases("""
            [База]
            ID=base-guid
            Folder=/
            Connect=Srvr="srv-db";Ref="База";
            App=1CV8
            DefaultApp=Auto

            [Новая]
            ID=new-guid
            Folder=/
            Connect=Srvr="srv-db";Ref="Новая";
            App=1cv8c
            DefaultApp=Auto

            """);

        var infobases = new List<Infobase>
        {
            new()
            {
                Name = "База",
                Id = "base-guid",
                LaunchMode = "Автоматический",
                Connection = new ConnectionSettings
                {
                    Type = ConnectionType.ClientServer,
                    Server = "srv-app",
                    DatabaseName = "База"
                }
            }
        };

        IbasesV8iImporter.Import(filePath, infobases, new List<Group>());

        var existing = Assert.Single(infobases, b => b.Name == "База");
        Assert.Equal("Толстый клиент", existing.LaunchMode);
        var added = Assert.Single(infobases, b => b.Name == "Новая");
        Assert.Equal("Тонкий клиент", added.LaunchMode);
    }

    [Fact]
    public void Import_RoundTripViaExporter_PreservesLaunchMode()
    {
        // Круговой путь приложения: экспорт «Толстый клиент» (App=ThickClient) →
        // импорт обратно — режим не теряется.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-rt-{Guid.NewGuid():N}.v8i");
        try
        {
            var source = new List<Infobase>
            {
                new()
                {
                    Name = "База",
                    Id = "base-guid",
                    LaunchMode = "Толстый клиент",
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.ClientServer,
                        Server = "srv-db",
                        DatabaseName = "База"
                    }
                }
            };

            IbasesV8iExporter.Export(filePath, source, new List<Group>());

            var imported = new List<Infobase>();
            IbasesV8iImporter.Import(filePath, imported, new List<Group>());

            Assert.Equal("Толстый клиент", Assert.Single(imported).LaunchMode);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Import_NewBaseWithoutMode_GetsAutomatic()
    {
        // Новая база без явного режима получает «Автоматический» (как раньше):
        // защита от затирания касается только СУЩЕСТВУЮЩИХ баз.
        var filePath = WriteIbases("""
            [Новая]
            ID=new-guid
            Folder=/
            Connect=Srvr="srv-db";Ref="Новая";
            App=Auto
            DefaultApp=Auto

            """);

        var imported = new List<Infobase>();
        IbasesV8iImporter.Import(filePath, imported, new List<Group>());

        Assert.Equal("Автоматический", Assert.Single(imported).LaunchMode);
    }

    private static string WriteIbases(string content)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-lm-{Guid.NewGuid():N}.v8i");
        File.WriteAllText(filePath, content, Encoding.Default);
        return filePath;
    }
}
