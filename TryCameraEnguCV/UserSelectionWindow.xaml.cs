using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace TryCameraEnguCV
{
    public partial class UserSelectionWindow : Window
    {
        private readonly string _configDir;
        private readonly string _usersFile;
        private List<User> _users = new();

        public UserSelectionWindow()
        {
            InitializeComponent();

            // Путь к AppData\BUSV\Config
            _configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BUSV", "Config");

            _usersFile = Path.Combine(_configDir, "users.json");

            Directory.CreateDirectory(_configDir);
            LoadUsers();
        }

        private void LoadUsers()
        {
            if (File.Exists(_usersFile))
            {
                string json = File.ReadAllText(_usersFile);
                _users = JsonSerializer.Deserialize<List<User>>(json) ?? new();
            }

            UserList.ItemsSource = _users;
            UserList.Items.Refresh();
        }

        private void SaveUsers()
        {
            Directory.CreateDirectory(_configDir);
            string json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_usersFile, json);
        }

        private void CreateUser_Click(object sender, RoutedEventArgs e)
        {
            if (_users.Count >= 5)
            {
                MessageBox.Show("Максимум 5 пользователей.", "Ошибка");
                return;
            }

            string name = Microsoft.VisualBasic.Interaction.InputBox("Введите имя пользователя:", "Создание пользователя", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            if (_users.Any(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Такой пользователь уже существует.", "Ошибка");
                return;
            }

            _users.Add(new User { Name = name });
            SaveUsers();
            LoadUsers();
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (UserList.SelectedItem is User user)
            {
                if (MessageBox.Show($"Удалить пользователя '{user.Name}'?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _users.Remove(user);
                    SaveUsers();
                    LoadUsers();
                }
            }
            else
            {
                MessageBox.Show("Выберите пользователя для удаления.");
            }
        }

        private void SelectUser_Click(object sender, RoutedEventArgs e)
        {
            if (UserList.SelectedItem is User user)
            {
                // Создаём главное окно
                var main = new MainWindow(user);

                // Устанавливаем его главным окном приложения
                Application.Current.MainWindow = main;

                // Показываем главное окно
                main.Show();

                // Закрываем окно выбора пользователя
                this.Close();
            }
            else
            {
                MessageBox.Show("Выберите пользователя.");
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            SaveUsers();
            Application.Current.Shutdown();
        }

    }
}
