﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using MailBee.ImapMail;
using MailBee.Mime;
using Mailer.Controls;
using Mailer.Helpers;
using Mailer.Messages;
using Mailer.Model;
using Mailer.Services;
using Mailer.View.Flyouts;

namespace Mailer.ViewModel.Main
{
    public class MainPageViewModel : ViewModelBase
    {
        private FolderCollection _folders;
        private List<FolderExtended> _foldersExtended;
        private bool _isMessagesLoading;
        private int _selectedFolder;
        private bool _isLoadMoreButtonVisible;
        private bool _atListBottom;
        private bool _isChecked;

        public event EventHandler SelectedFolderNameChanged;

        public ObservableCollection<ContextAction> Actions { get; set; }

        public MainPageViewModel()
        {
            IsWorking = true;
            Actions = new ObservableCollection<ContextAction>();
            InitializeCommands();
            SelectedFolderNameChanged += OnSelectedFolderChanged;
            LoadInfo();
        }


        public RelayCommand GoToSettingsCommand { get; private set; }
        public RelayCommand AddFolderCommand { get; private set; }
        public RelayCommand<EnvelopeWarpper> ReadEmailCommand { get; private set; }
        public RelayCommand<int> LoadMoreCommand { get; private set; }
        public RelayCommand<EnvelopeWarpper> DeleteMessageCommand { get; private set; }
        public RelayCommand<EnvelopeWarpper> MarkMessageCommand { get; private set; }
        public RelayCommand DeleteMessagesCommand { get; private set; }
        public RelayCommand<MarkAs> MarkMessagesCommand { get; private set; }
        public RelayCommand ClearFolderCommand { get; set; }
        public RelayCommand DeleteFolderCommand { get; set; }
        public RelayCommand NewMessageCommand { get; private set; }
        public int SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                Set(ref _selectedFolder, value);
                OnSelectedFolderChanged(EventArgs.Empty);
            }
        }

        protected virtual void OnSelectedFolderChanged(EventArgs e)
        {
            SelectedFolderNameChanged?.Invoke(this, e);
        }

        private async void OnSelectedFolderChanged(object sender, EventArgs eventArgs)
        {
            if (FoldersExtended == null) return;
            if (FoldersExtended[_selectedFolder].EnvelopeCollection == null ||
                FoldersExtended[_selectedFolder].EnvelopeCollection.Count == 0)
            {
                IsMessagesLoading = true;
                AtListBottom = false;
                IsLoadMoreButtonVisible = false;
                await LoadFolderMessages(_selectedFolder);
                IsMessagesLoading = false;
            }
            else
            {
                ChangeFolder(_selectedFolder);
            }
            RaisePropertyChanged($"MailEnvelopeCollection");
        }

        public FolderCollection Folders
        {
            get => _folders;
            set => Set(ref _folders, value);
        }

        public List<FolderExtended> FoldersExtended
        {
            get => _foldersExtended;
            set => Set(ref _foldersExtended, value);
        }

        public bool IsMessagesLoading
        {
            get => _isMessagesLoading;
            set => Set(ref _isMessagesLoading, value);
        }

        public bool IsLoadMoreButtonVisible
        {
            get => _isLoadMoreButtonVisible;
            set => Set(ref _isLoadMoreButtonVisible, value);
        }

        public bool AtListBottom
        {
            get => _atListBottom;
            set
            {
                Set(ref _atListBottom, value);
                if (value && FoldersExtended[SelectedFolder].LastLoadedIndex > 1)
                    IsLoadMoreButtonVisible = true;
                else
                    IsLoadMoreButtonVisible = false;
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                Set(ref _isChecked, value);
                foreach (var item in FoldersExtended[SelectedFolder].EnvelopeCollection)
                {
                    item.IsChecked = IsChecked;
                }
            }
        }

        public ObservableCollection<EnvelopeWarpper> MailEnvelopeCollection => _foldersExtended == null
            ? null
            : FoldersExtended[SelectedFolder].EnvelopeCollection;

        private void InitializeCommands()
        {
            GoToSettingsCommand = new RelayCommand(() =>
            {
                Messenger.Default.Send(new NavigateToPageMessage
                {
                    Page = "/Settings.SettingsView"
                });
            });
            AddFolderCommand = new RelayCommand(CreateFolder);
            DeleteFolderCommand = new RelayCommand(DeleteFolder);
            ClearFolderCommand = new RelayCommand(ClearFolder);
            ReadEmailCommand = new RelayCommand<EnvelopeWarpper>(ReadEmail);
            LoadMoreCommand = new RelayCommand<int>(LoadMore);
            DeleteMessageCommand = new RelayCommand<EnvelopeWarpper>(DeleteMessage);
            MarkMessageCommand = new RelayCommand<EnvelopeWarpper>(MarkMessage);
            DeleteMessagesCommand = new RelayCommand(DeleteMessages);
            MarkMessagesCommand = new RelayCommand<MarkAs>(MarkMessages);
            NewMessageCommand = new RelayCommand(NewMessage);
        }

        private async void LoadInfo()
        {
            IsWorking = true;
            await LoadFolders();
            IsWorking = false;
            IsMessagesLoading = true;
            AtListBottom = false;
            IsLoadMoreButtonVisible = false;
            await LoadFolderMessages(SelectedFolder);
            IsMessagesLoading = false;
            RaisePropertyChanged($"MailEnvelopeCollection");
        }

        private async void LoadMore(int folderIndex)
        {
            IsMessagesLoading = true;
            AtListBottom = false;
            IsLoadMoreButtonVisible = false;
            await LoadFolderMessages(folderIndex);
            IsMessagesLoading = false;
        }

        private async Task LoadFolderMessages(int folderIndex)
        {
            await LoadMessages(folderIndex);
        }

        public async void ChangeFolder(int folderIndex)
        {
            await ImapService.ImapClient.SelectFolderAsync(FoldersExtended[folderIndex].Name);
        }

        public async Task LoadMessages(int folderIndex)
        {
            try
            {
                var folder = FoldersExtended[folderIndex];
                await ImapService.ImapClient.SelectFolderAsync(folder.Name);
                var msgCount = folder.LastLoadedIndex > 50 ? 50 : folder.LastLoadedIndex;
                if (folder.EnvelopeCollection == null)
                    folder.EnvelopeCollection = new ObservableCollection<EnvelopeWarpper>();
                var range = folder.LastLoadedIndex + ":" + (folder.LastLoadedIndex - msgCount + 1);
                var envelopes = await ImapService.ImapClient.DownloadEnvelopesAsync(range, false,
                    EnvelopeParts.BodyStructure | EnvelopeParts.MessagePreview | EnvelopeParts.InternalDate | EnvelopeParts.Flags | EnvelopeParts.Uid,
                    1000);
                folder.LastLoadedIndex = folder.LastLoadedIndex - msgCount + 1;

                envelopes.Reverse();

                foreach (Envelope item in envelopes)
                {
                    folder.EnvelopeCollection.Add(new EnvelopeWarpper(item));
                }

                RaisePropertyChanged($"MailEnvelopeCollection");
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        public async Task LoadFolders()
        {
            try
            {
                FoldersExtended = null;
                Actions = new ObservableCollection<ContextAction>();
                Folders = await ImapService.ImapClient.DownloadFoldersAsync();
                var tmpFldrs = new List<FolderExtended>();
                foreach (Folder item in Folders)
                {
                    var folderInfo = await ImapService.ImapClient.GetFolderStatusAsync(item.Name);
                    var folderExtended = new FolderExtended(item.Name, item.ShortName, folderInfo.MessageCount, folderInfo.UnseenCount);
                    Actions.Add(new ContextAction(folderExtended.Name, new RelayCommand<string>(MoveMessages)));
                    tmpFldrs.Add(folderExtended);
                }
                FoldersExtended = tmpFldrs;
                RaisePropertyChanged($"Actions");
                RaisePropertyChanged($"MailEnvelopeCollection");
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        public async Task UpdateFolders()
        {
            try
            {
                foreach (var folder in FoldersExtended)
                {
                    var folderInfo = await ImapService.ImapClient.GetFolderStatusAsync(folder.Name);
                    folder.MessagesCount = folderInfo.MessageCount;
                    folder.UnreadedMessagesCount = folderInfo.UnseenCount;
                    if (FoldersExtended[SelectedFolder].Name == folder.Name) continue;
                    folder.EnvelopeCollection = null;
                    folder.LastLoadedIndex = folderInfo.MessageCount;
                }
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        public async void CreateFolder()
        {
            var flyout = new FlyoutControl { FlyoutContent = new CreateFolderView() };
            var result = (bool) await flyout.ShowAsync();
            if (!result) return;
            LoadInfo();
        }


        public async void DeleteFolder()
        {
            try
            {
                var flyout = new FlyoutControl { FlyoutContent = new ConfirmView($"Delete {FoldersExtended[SelectedFolder].Name} folder?") };
                var result = (bool)await flyout.ShowAsync();
                if (!result) return;
                await ImapService.ImapClient.DeleteFolderAsync(FoldersExtended[SelectedFolder].Name);
                LoadInfo();
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        private async void ClearFolder()
        {
            try
            {
                var flyout = new FlyoutControl { FlyoutContent = new ConfirmView($"Clear {FoldersExtended[SelectedFolder].Name} folder?") };
                var result = (bool)await flyout.ShowAsync();
                if (!result) return;
                await ImapService.ImapClient.DeleteMessagesAsync("1:*", false);
                LoadInfo();
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        public async void ReadEmail(EnvelopeWarpper envelope)
        {
            var flyout = new FlyoutControl { FlyoutContent = new MessageView(envelope) };
            var result = await flyout.ShowAsync();
            await UpdateFolders();
        }

        public async void DeleteMessage(EnvelopeWarpper envelope)
        {
            try
            {
                await ImapService.DeleteMessageAsync(Convert.ToString(envelope.Uid));
                FoldersExtended[SelectedFolder].EnvelopeCollection.Remove(envelope);
                await UpdateFolders();
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }


        private void MarkMessage(EnvelopeWarpper obj)
        {
            try
            {
                obj.IsUnseen = !obj.IsUnseen;
                if (obj.IsUnseen)
                    FoldersExtended[SelectedFolder].UnreadedMessagesCount++;
                else
                    FoldersExtended[SelectedFolder].UnreadedMessagesCount--;
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        private async void DeleteMessages()
        {
            try
            {
                var uidsList = new List<string>();
                foreach (var item in FoldersExtended[SelectedFolder].EnvelopeCollection.Reverse())
                {
                    if (!item.IsChecked) continue;
                    uidsList.Add(Convert.ToString(item.Uid));
                    FoldersExtended[SelectedFolder].EnvelopeCollection.Remove(item);
                }
                if (uidsList.Count == 0) return;
                await ImapService.DeleteMessageAsync(uidsList);
                await UpdateFolders();
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        private async void MoveMessages(string folder)
        {
            try
            {
                var uidsList = new List<string>();
                foreach (var item in FoldersExtended[SelectedFolder].EnvelopeCollection.Reverse())
                {
                    if (!item.IsChecked) continue;
                    uidsList.Add(Convert.ToString(item.Uid));
                    FoldersExtended[SelectedFolder].EnvelopeCollection.Remove(item);
                }
                if (uidsList.Count == 0) return;
                await ImapService.MoveMessageAsync(uidsList, folder);
                await UpdateFolders();
            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        private async void MarkMessages(MarkAs markAs)
        {
            try
            {
                var uidsList = new List<string>();
                SystemMessageFlags systemMessageFlag;
                MessageFlagAction messageFlagAction;
                if (markAs == MarkAs.Readed)
                {
                    systemMessageFlag = SystemMessageFlags.Seen;
                    messageFlagAction = MessageFlagAction.Add;
                }
                else
                {
                    systemMessageFlag = SystemMessageFlags.Seen;
                    messageFlagAction = MessageFlagAction.Remove;
                }
                foreach (var item in FoldersExtended[SelectedFolder].EnvelopeCollection)
                {
                    if (!item.IsChecked) continue;
                    uidsList.Add(Convert.ToString(item.Uid));
                    switch (markAs)
                    {
                        case MarkAs.Readed:
                            item.IsUnseenSilent = false;
                            break;
                        case MarkAs.Unreaded:
                            item.IsUnseenSilent = true;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(markAs), markAs, null);
                    }
                }
                if (uidsList.Count == 0) return;
                await ImapService.MarkMessages(uidsList, systemMessageFlag, messageFlagAction);

            }
            catch (Exception e)
            {
                LoggingService.Log(e);
            }
        }

        private async void NewMessage()
        {
            
        }
    }
}