using System.Windows;
using System.Windows.Controls;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class NotesWindow : Window
{
    private readonly DatabaseService _db = DatabaseService.Instance;
    private List<Note> _allNotes = [];
    private Note? _currentNote;

    public NotesWindow()
    {
        InitializeComponent();
        DpDate.SelectedDate = DateTime.Today;
        LoadNotes();
    }

    private void LoadNotes()
    {
        _allNotes = _db.GetAllNotes();
        LbNotes.ItemsSource = null;
        LbNotes.ItemsSource = _allNotes;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string kw = TxtSearch.Text.Trim();
        if (string.IsNullOrEmpty(kw))
        {
            LbNotes.ItemsSource = _allNotes;
        }
        else
        {
            LbNotes.ItemsSource = _db.SearchNotes(kw);
        }
    }

    private void LbNotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LbNotes.SelectedItem is Note note)
        {
            _currentNote = note;
            TxtTitle.Text = note.Title;
            TxtContent.Text = note.Content;
            if (DateTime.TryParse(note.NoteDate, out var dt))
                DpDate.SelectedDate = dt;
            LblStatus.Text = string.Empty;
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        _currentNote = null;
        TxtTitle.Text = string.Empty;
        TxtContent.Text = string.Empty;
        DpDate.SelectedDate = DateTime.Today;
        LbNotes.SelectedItem = null;
        LblStatus.Text = "新規メモを作成";
        TxtTitle.Focus();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote == null) return;
        var result = MessageBox.Show(
            $"「{_currentNote.Preview}」を削除しますか？",
            "メモ削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _db.DeleteNote(_currentNote.Id);
        _currentNote = null;
        TxtTitle.Text = string.Empty;
        TxtContent.Text = string.Empty;
        DpDate.SelectedDate = DateTime.Today;
        LoadNotes();
        LblStatus.Text = "削除しました";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string title = TxtTitle.Text.Trim();
        string content = TxtContent.Text.Trim();
        string date = DpDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
        {
            LblStatus.Text = "タイトルまたは内容を入力してください";
            return;
        }

        if (_currentNote != null)
        {
            _currentNote.Title = title;
            _currentNote.Content = content;
            _currentNote.NoteDate = date;
            _db.UpdateNote(_currentNote);
            LblStatus.Text = "メモを更新しました";
        }
        else
        {
            var note = new Note
            {
                Title = title,
                Content = content,
                NoteDate = date,
            };
            _db.InsertNote(note);
            LblStatus.Text = "メモを保存しました";
        }

        LoadNotes();
        // 編集したメモを選択状態にする
        if (_currentNote != null)
        {
            foreach (var n in _allNotes)
            {
                if (n.Id == _currentNote.Id)
                {
                    LbNotes.SelectedItem = n;
                    break;
                }
            }
        }
    }
}
