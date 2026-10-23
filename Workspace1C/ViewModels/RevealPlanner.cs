using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Чистая логика планирования «перехода к строке» (issue #31): выбор домашнего узла
/// (leaf), в поддереве которого ищется целевая строка, и проверка достижимости цели.
/// Команды «Найти в списке» (Ctrl+T) и переходы по закладкам (Ctrl+цифра) должны
/// выделять ДОМАШНЮЮ строку базы — в её настоящей группе во «Все базы», узле
/// «Без группы» или плоском узле — а НЕ первую попавшуюся копию в «Закреплённых».
/// <para>
/// Вынесена в отдельный статический класс, чтобы WPF (<see cref="Workspace1C.MainWindow"/>)
/// и Avalonia использовали одну и ту же планирующую логику, а <c>GoToRowTests</c> покрывали
/// её чистыми юнит-тестами без окон и диспетчера (конвенция проекта: селекторы и
/// оркестрация тестируются через чистые статические помощники).
/// </para>
/// </summary>
public static class RevealPlanner
{
    /// <summary>
    /// Результат планирования reveal: <see cref="Leaf"/> — домашний узел, в поддереве
    /// которого следует искать строку цели (null, если цели нет в дереве либо при
    /// revealToHome она представлена только копией в «Закреплённых»);
    /// <see cref="Reachable"/> — достижима ли цель в текущем дереве данных.
    /// </summary>
    public sealed record RevealPlan(GroupNodeViewModel? Leaf, bool Reachable);

    /// <summary>
    /// Планирует reveal для цели. При <paramref name="revealToHome"/> (команда «Найти
    /// в списке»/закладка) цель достижима только если найден домашний узел ВНЕ узла
    /// «Закреплённые»: закреплённая база присутствует в дереве дважды, и «переход»
    /// обязан вести в общий список, а не на копию. Для обычных пересборок (ложный случай)
    /// достижимость — простое присутствие базы/группы в дереве данных.
    /// </summary>
    public static RevealPlan Plan(
        IEnumerable<GroupNodeViewModel> roots, object target, bool revealToHome)
    {
        var leaf = PlanLeaf(roots, target, revealToHome);
        if (revealToHome)
        {
            // Группа-цель не дублируется в «Закреплённых»: для КОРНЕВОЙ группы leaf
            // (родитель) равен null, но цель в дереве есть — допускаем её.
            var reachable = leaf is not null
                || (target is GroupNodeViewModel && IsTargetPresent(roots, target));
            return new RevealPlan(leaf, reachable);
        }
        return new RevealPlan(leaf, IsTargetPresent(roots, target));
    }

    /// <summary>
    /// Вычисляет домашний узел (leaf) для цели:
    /// <list type="bullet">
    /// <item>база при revealToHome — узел во «Все базы» (настоящая группа, «Без группы»
    /// или плоский узел), см. <see cref="GroupNodeViewModel.FindInfobaseHomeNode"/> —
    /// узел «Закреплённые» пропускается;</item>
    /// <item>база при обычной пересборке — ПЕРВЫЙ узел, содержащий базу (в т.ч. копия
    /// в «Закреплённых») — поведение прежнее;</item>
    /// <item>группа — её родитель (цепочка раскрытия предков);</item>
    /// <item>прочие цели (спец-узлы) — null.</item>
    /// </list>
    /// </summary>
    public static GroupNodeViewModel? PlanLeaf(
        IEnumerable<GroupNodeViewModel> roots, object target, bool revealToHome)
    {
        return target switch
        {
            Infobase ib => revealToHome
                ? GroupNodeViewModel.FindInfobaseHomeNode(roots, ib)
                : FindNodeByInfobase(roots, ib),
            GroupNodeViewModel gn => gn.Parent,
            _ => null
        };
    }

    /// <summary>Присутствует ли цель в текущем дереве данных (без визуального дерева): база —
    /// размещена в каком-либо узле (в т.ч. «Закреплённые»), группа — найдена по Id модели.</summary>
    public static bool IsTargetPresent(IEnumerable<GroupNodeViewModel> roots, object target)
    {
        return target switch
        {
            Infobase ib => FindNodeByInfobase(roots, ib) is not null,
            GroupNodeViewModel gn => gn.Group is not null
                && FindGroupNodeById(roots, gn.Group.Id) is not null,
            _ => false
        };
    }

    private static GroupNodeViewModel? FindNodeByInfobase(
        IEnumerable<GroupNodeViewModel> roots, Infobase infobase)
    {
        // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
        // reveal обязан искать цель только среди видимых узлов.
        foreach (var root in roots)
        {
            if (!root.IsVisible)
                continue;
            var found = FindInNode(root, infobase);
            if (found is not null)
                return found;
        }
        return null;

        static GroupNodeViewModel? FindInNode(GroupNodeViewModel node, Infobase ib)
        {
            foreach (var child in node.Children)
            {
                var found = FindInNode(child, ib);
                if (found is not null)
                    return found;
            }
            if (node.Infobases.Any(item => ReferenceEquals(item, ib)))
                return node;
            return null;
        }
    }

    private static GroupNodeViewModel? FindGroupNodeById(
        IEnumerable<GroupNodeViewModel> roots, string id)
    {
        // Скрытые корни (issue #21, Этап F) остаются в GroupNodes, но не отображаются:
        // reveal обязан искать цель только среди видимых узлов.
        foreach (var root in roots)
        {
            if (!root.IsVisible)
                continue;
            var found = FindById(root, id);
            if (found is not null)
                return found;
        }
        return null;

        static GroupNodeViewModel? FindById(GroupNodeViewModel node, string id)
        {
            if (node.Group is not null
                && string.Equals(node.Group.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
            foreach (var child in node.Children)
            {
                var found = FindById(child, id);
                if (found is not null)
                    return found;
            }
            return null;
        }
    }
}