using System;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Сменить платформу» (issue #54, переработка, Windows/WPF): выбор версии
    /// платформы 1С из установленных и замена её у ВЫДЕЛЕННЫХ баз по «ОК». Вся логика —
    /// в чистой <see cref="PlatformChangeViewModel"/>; окно — тонкая обёртка: поля
    /// привязаны к VM, по «Сменить» выполняет команду, закрывается с ОК.
    /// </summary>
    public partial class PlatformChangeWindow : EscClosableWindow
    {
        private readonly PlatformChangeViewModel _vm;

        /// <param name="vm">Готовый ViewModel окна (кандидаты и колбэк формирует MainViewModel).</param>
        public PlatformChangeWindow(PlatformChangeViewModel vm)
        {
            InitializeComponent();
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("PlatformChange.Title");
        }

        private void OnApply_Click(object sender, RoutedEventArgs e)
        {
            _vm.ApplyCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}