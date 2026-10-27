using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Миграция между кластерами 1С» (issue #41, переработка по комментарию 7OH,
    /// Windows/WPF): перенос информационных баз между кластерами серверов 1С. Двухпанельная
    /// раскладка «Источник»/«Приёмник» (сервер + кластер + статусы), чеклист баз источника
    /// со статусами источника/приёмника, опции «Создавать группу по имени кластера» и
    /// «Не удалять в старом кластере», учётные данные СУБД и кнопка «Мигрировать».
    /// Вся логика — в чистой ViewModel <see cref="ClusterMigrationViewModel"/>; окно —
    /// тонкая обёртка (источник серверов, подстановка выбранного сервера, вопрос
    /// «Хотите заменить строку подключения?» после успешного переноса).
    /// После <c>ShowDialog()==true</c> результат доступен через <see cref="Result"/>.
    /// </summary>
    public partial class ClusterMigrationWindow : Window
    {
        private readonly ClusterMigrationViewModel _vm;

        /// <param name="rac">Клиент rac (кластеры, «infobase summary list/create/drop»).</param>
        /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для статуса источника.</param>
        /// <param name="availableServers">Справочник «Серверы 1С» (полный список для обеих панелей).</param>
        /// <param name="initialSourceServer">Сервер, открытый из окна «Серверы 1С» (issue #41); null — обычное открытие.</param>
        /// <param name="initialSourceClusterId">Кластер, выбранный в окне «Серверы 1С» (issue #41).</param>
        public ClusterMigrationWindow(
            IRacClient rac,
            IReadOnlyList<Infobase> existingInfobases,
            IReadOnlyList<ManagementServer> availableServers,
            ManagementServer? initialSourceServer = null,
            Guid? initialSourceClusterId = null)
        {
            InitializeComponent();

            _vm = new ClusterMigrationViewModel(
                rac,
                existingInfobases,
                availableServers,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("ClusterMigration.Title");
            BasesGrid.ItemsSource = _vm.SourceRows;

            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;
            UpdateCommandStates();

            // Открытие из окна «Серверы 1С»: выбранный сервер-источник + автоподключение
            // (issue #41): сопоставление с записями справочника по Id — в VM, чтобы ComboBox
            // подсвечивал текущий сервер (у ManagementServer нет переопределённого Equals).
            _vm.SetInitialSource(initialSourceServer, initialSourceClusterId);
        }

        /// <summary>
        /// Результат миграции (при <c>ShowDialog()==true</c>): перенесённые базы, базы для
        /// добавления в список приложения, новый сервер/порт и ответ на вопрос о замене
        /// строки подключения.
        /// </summary>
        public ClusterMigrationResult? Result => _vm.Result;

        private void OnSourceConnect_Click(object sender, RoutedEventArgs e) =>
            _vm.ConnectSourceCommand.Execute(null);

        private void OnSourceLoadBases_Click(object sender, RoutedEventArgs e) =>
            _vm.LoadSourceBasesCommand.Execute(null);

        private void OnTargetConnect_Click(object sender, RoutedEventArgs e) =>
            _vm.ConnectTargetCommand.Execute(null);

        private void OnTargetGetStatuses_Click(object sender, RoutedEventArgs e) =>
            _vm.GetTargetStatusesCommand.Execute(null);

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectAllCommand.Execute(null);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectNoneCommand.Execute(null);

        private void OnDbPasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.DbPassword = DbPasswordBox.Password;

        private void OnMigrate_Click(object sender, RoutedEventArgs e) =>
            _vm.MigrateCommand.Execute(null);

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterMigrationViewModel.IsBusy):
                case nameof(ClusterMigrationViewModel.HasAvailableServers):
                case nameof(ClusterMigrationViewModel.SelectedSourceServer):
                case nameof(ClusterMigrationViewModel.SelectedTargetServer):
                case nameof(ClusterMigrationViewModel.SelectedSourceClusterId):
                case nameof(ClusterMigrationViewModel.SelectedTargetClusterId):
                case nameof(ClusterMigrationViewModel.CheckedCount):
                case nameof(ClusterMigrationViewModel.HasCheckedInTarget):
                case nameof(ClusterMigrationViewModel.ServersOverlap):
                case nameof(ClusterMigrationViewModel.ProgressText):
                    UpdateCommandStates();
                    break;
                case nameof(ClusterMigrationViewModel.MigrationCompleted):
                    OnMigrationCompleted();
                    break;
            }
        }

        /// <summary>
        /// Единый пересчёт кнопок и индикатора занятости из состояния VM: «Подключиться»
        /// доступна при выбранном сервере и неактивном запросе; «Получить базы»/«Получить
        /// статусы» — при выбранном кластере; «Мигрировать» — по <see cref="ClusterMigrationViewModel.CanMigrate"/>.
        /// </summary>
        private void UpdateCommandStates()
        {
            var busy = _vm.IsBusy;
            SourceConnectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            SourceLoadBasesButton.IsEnabled = !busy && _vm.SelectedSourceClusterId is not null;
            TargetConnectButton.IsEnabled = !busy && _vm.HasAvailableServers;
            TargetGetStatusesButton.IsEnabled = !busy && _vm.SelectedTargetClusterId is not null && _vm.SourceRows.Count > 0;
            SelectAllButton.IsEnabled = !busy;
            SelectNoneButton.IsEnabled = !busy;
            MigrateButton.IsEnabled = _vm.CanMigrate;
            BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Text = _vm.ProgressText;
        }

        /// <summary>
        /// Перенос завершён (issue #41, комментарий 7OH от 2026-10-26 «закрывать окно
        /// не надо»): вопрос «Хотите заменить строку подключения?» → если ДА — результат
        /// помечается (MainViewModel заменит сервер/порт в списке приложения). Окно НЕ
        /// закрывается: строки со статусами и сводка «Перенесено: N · Ошибок: M» видны,
        /// можно повторить перенос (например, на другой кластер-приёмник) — перенесённые
        /// базы накапливаются в <see cref="Result"/>. Закрытие — только кнопкой «Закрыть»
        /// или крестиком; MainViewModel применяет результат после закрытия окна.
        /// </summary>
        private void OnMigrationCompleted()
        {
            if (_vm.Result is null)
                return;

            var answer = MessageBox.Show(
                LocalizationManager.T("ClusterMigration.ConfirmReplaceConnection"),
                LocalizationManager.T("ClusterMigration.ReplaceConnectionTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                _vm.SetReplaceConnectionStrings(true);
        }
    }
}
