using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Регистрация задания «Развернуть тестовую копию» в планировщике (<see cref="SchedulerService"/>):
/// по флагу <see cref="TestDeployScenario.UseSchedule"/> создаёт <see cref="ScheduledTask"/> типа
/// <see cref="ScheduledTaskKind.TestDeploy"/> с ссылкой на сценарий (<see cref="ScheduledTask.ScenarioId"/>).
/// «Каждый понедельник» и дальше настраивается в стандартном окне «Задания по расписанию».
/// </summary>
public static class TestDeployScheduleHelper
{
    /// <summary>
    /// Создаёт задание по расписанию для сценария (если <see cref="TestDeployScenario.UseSchedule"/>).
    /// Возвращает созданное задание или null. Ошибки сохранения не роняют развёртывание.
    /// </summary>
    public static ScheduledTask? RegisterTask(
        TestDeployScenario scenario,
        IScheduledTaskStore? store = null,
        SchedulerService? scheduler = null)
    {
        if (scenario is null || !scenario.UseSchedule)
            return null;
        var taskStore = store ?? AppServices.TryGetService<IScheduledTaskStore>();
        if (taskStore is null)
            return null;

        var task = new ScheduledTask
        {
            Name = string.IsNullOrWhiteSpace(scenario.Name)
                ? (string.IsNullOrWhiteSpace(scenario.TargetName) ? "Тестовая копия" : scenario.TargetName)
                : scenario.Name,
            Kind = ScheduledTaskKind.TestDeploy,
            Enabled = true,
            Time = string.IsNullOrWhiteSpace(scenario.ScheduleTime)
                ? "06:00"
                : scenario.ScheduleTime.Trim(),
            DaysOfWeek = scenario.ScheduleDaysOfWeek ?? new List<DayOfWeek>(),
            ScenarioId = scenario.Id
        };
        try
        {
            taskStore.Save(task);
            (scheduler ?? AppServices.TryGetService<SchedulerService>())?.Refresh();
            return task;
        }
        catch (Exception ex)
        {
            // Несохранение задания не должно ломать успешное развёртывание.
            AppServices.TryGetService<IAppLogger>()?.Warn(
                $"Не удалось сохранить задание тест-копии «{task.Name}»: {ex.Message}");
            return null;
        }
    }
}