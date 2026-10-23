using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Предопределённый (встроенный) набор типовых конфигураций 1С. Используется окном
/// «Актуальные релизы» и окном настройки связи ИБ ↔ конфигурация как стартовый список.
/// Пользователь может добавлять собственные конфигурации (хранятся в
/// <c>AppSettings.CustomConfigTypes</c>) — они объединяются со встроенными при показе.
/// Встроенный список дополнен по PR #43 (7OH): Бухгалтерия КОРП, Документооборот
/// ПРОФ/КОРП, Зарплата и управление персоналом КОРП, рабочее место кассира, УНФ, УПП;
/// редакции и каталоги релизов приведены к актуальным (issue #321, 1.0.0.52).
/// </summary>
public static class BuiltInConfigTypes
{
    /// <summary>Единственный экземпляр предопределённого набора (неизменяемый).</summary>
    public static IReadOnlyList<OneCConfigType> All { get; } = Build();

    private static List<OneCConfigType> Build()
    {
        var list = new List<OneCConfigType>
        {
            // Бухгалтерия предприятия — наиболее распространённая типовая конфигурация.
            // Ники конфигураций/редакций — по списку 7OH (issue #321, комментарий 5952509522;
            // PR #43, 1.0.0.52): база каталога Accounting; редакции 2.x/3.x имеют собственные
            // каталоги Accounting20_82/Accounting30 (UrlOverride — конечная часть адреса
            // releases.1c.ru/project/<код>, полная ссылка собирается автоматически).
            new OneCConfigType
            {
                Code = "BP",
                Name = "Бухгалтерия предприятия",
                ConfigName = "БухгалтерияПредприятия",
                UrlCode = "Бухгалтерия предприятия",
                Nick = "Accounting30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "Accounting20_82",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "Accounting30",
                        IsDefault = true,
                    },
                },
            },
            // Бухгалтерия предприятия КОРП.
            new OneCConfigType
            {
                Code = "BPCorp",
                Name = "Бухгалтерия предприятия КОРП",
                ConfigName = "БухгалтерияПредприятияКОРП",
                UrlCode = "Бухгалтерия предприятия КОРП",
                Nick = "AccountingCorp30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "AccountingCorp",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "AccountingCorp30",
                        IsDefault = true,
                    },
                },
            },
            // Документооборот ПРОФ
            new OneCConfigType
            {
                Code = "DOPROF",
                Name = "Документооборот ПРОФ",
                ConfigName = "ДокументооборотПРОФ",
                UrlCode = "Документооборот ПРОФ",
                Nick = "DocMng30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "DocMng",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "DocMng30",
                        IsDefault = true,
                    },
                },
            },
            // Документооборот КОРП
            new OneCConfigType
            {
                Code = "DOCORP",
                Name = "Документооборот КОРП",
                ConfigName = "ДокументооборотКОРП",
                UrlCode = "Документооборот КОРП",
                Nick = "DocMngCorp30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "DocMngCorp",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "DocMngCorp30",
                        IsDefault = true,
                    },
                },
            },
            // Зарплата и управление персоналом.
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                ConfigName = "ЗарплатаИУправлениеПерсоналом",
                UrlCode = "Зарплата и управление персоналом",
                Nick = "HRM30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "HRM",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "HRM30",
                        IsDefault = true,
                    },
                },
            },
            // Зарплата и управление персоналом - КОРП.
            new OneCConfigType
            {
                Code = "ZUPCORP",
                Name = "Зарплата и управление персоналом КОРП",
                ConfigName = "ЗарплатаИУправлениеПерсоналомКОРП",
                UrlCode = "Зарплата и управление персоналом КОРП",
                Nick = "HRMCorp30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.x", Red = "2",
                        UrlOverride = "HRMCorp",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "HRMCorp30",
                        IsDefault = true,
                    },
                },
            },
            // Управление торговлей: каталог Trade, редакции 10.3/11.х — Trade103/Trade110.
            new OneCConfigType
            {
                Code = "UT",
                Name = "Управление торговлей",
                ConfigName = "УправлениеТорговлей",
                UrlCode = "Управление торговлей",
                Nick = "Trade110",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "10.3", Red = "10.3",
                        UrlOverride = "Trade103",
                    },
                    new OneCConfigEdition
                    {
                        Name = "11.x", Red = "11",
                        UrlOverride = "Trade110",
                        IsDefault = true
                    },
                },
            },
            // Комплексная автоматизация: каталог ARAutomation, редакции 1.0/1.1/2.0.
            new OneCConfigType
            {
                Code = "KA",
                Name = "Комплексная автоматизация",
                ConfigName = "КомплекснаяАвтоматизация",
                UrlCode = "Комплексная автоматизация",
                Nick = "ARAutomation20",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "1.0", Red = "1.0",
                        UrlOverride = "ARAutomation10",
                    },
                    new OneCConfigEdition
                    {
                        Name = "1.1", Red = "1.1",
                        UrlOverride = "ARAutomation11",
                    },
                    new OneCConfigEdition
                    {
                        Name = "2.0", Red = "2.0",
                        UrlOverride = "ARAutomation20",
                        IsDefault = true
                    },
                },
            },
            // Рабочее место кассира
            new OneCConfigType
            {
                Code = "RMK",
                Name = "Рабочее место кассира",
                ConfigName = "РМКБазовая",
                UrlCode = "Рабочее место кассира",
                Nick = "PayDesk",
                IsBuiltIn = true,
            },
            // 1С:ERP Управление предприятием 2.
            new OneCConfigType
            {
                Code = "ERP",
                Name = "1С:ERP Управление предприятием 2",
                ConfigName = "УправлениеПредприятием",
                UrlCode = "ERP Управление предприятием 2",
                Nick = "EnterpriseERP20",
                IsBuiltIn = true,
            },
            // Розница: каталог Retail, редакции 2.3/3.х — Retail23/Retail30.
            new OneCConfigType
            {
                Code = "Retail",
                Name = "Розница",
                ConfigName = "Retail",
                UrlCode = "Розница",
                Nick = "Retail30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.3", Red = "2.3",
                        UrlOverride = "Retail23",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "Retail30",
                        IsDefault = true
                    },
                },
            },
            // Управление нашей фирмой
            new OneCConfigType
            {
                Code = "UNF",
                Name = "Управление нашей фирмой",
                ConfigName = "УправлениеНашейФирмой",
                UrlCode = "Управление нашей фирмой",
                Nick = "SmallBusiness30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "1.6", Red = "1.6",
                        UrlOverride = "SmallBusiness16",
                    },
                    new OneCConfigEdition
                    {
                        Name = "3.x", Red = "3",
                        UrlOverride = "SmallBusiness30",
                        IsDefault = true
                    },
                },
            },
            // Управление производственным предприятием
            new OneCConfigType
            {
                Code = "UTP",
                Name = "Управление производственным предприятием",
                ConfigName = "УправлениеПроизводственнымПредприятием",
                UrlCode = "Управление производственным предприятием",
                Nick = "Enterprise13",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "1.2", Red = "1.2",
                        UrlOverride = "Enterprise12",
                    },
                    new OneCConfigEdition
                    {
                        Name = "1.3", Red = "1.3",
                        UrlOverride = "Enterprise13",
                        IsDefault = true
                    },
                },
            },
            // Бухгалтерия государственного учреждения.
            new OneCConfigType
            {
                Code = "BGU",
                Name = "Бухгалтерия государственного учреждения",
                ConfigName = "БухгалтерияГосударственногоУчреждения",
                UrlCode = "Бухгалтерия государственного учреждения",
                Nick = "StateAccounting20",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "1.x", Red = "1",
                        UrlOverride = "StateAccounting", },
                    new OneCConfigEdition { Name = "2.x", Red = "2",
                        UrlOverride = "StateAccounting20",
                        IsDefault = true },
                },
            },
        };

        // Сегменты URL принято кодировать латиницей/цифрами; имя с пробелами и кириллицей
        // дополнительно экранируется при формировании адреса (Uri.EscapeDataString).
        // UrlOverride редакций содержит конечную часть адреса после releases.1c.ru/project/
        // (семантика «полная ссылка или хвост», issue #369/#322) — полная ссылка
        // собирается автоматически в OneCUpdatesService.ResolveOverrideUrl.
        return list;
    }
}