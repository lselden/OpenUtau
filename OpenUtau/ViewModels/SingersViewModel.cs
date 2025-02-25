﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using Avalonia.Media.Imaging;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class SingersViewModel : ViewModelBase {
        public IEnumerable<USinger> Singers => SingerManager.Inst.SingerGroups.Values.SelectMany(l => l);
        [Reactive] public USinger? Singer { get; set; }
        [Reactive] public Bitmap? Avatar { get; set; }
        [Reactive] public string? Info { get; set; }
        public ObservableCollectionExtended<USubbank> Subbanks => subbanks;
        public ObservableCollectionExtended<UOto> Otos => otos;
        [Reactive] public UOto? SelectedOto { get; set; }
        [Reactive] public int SelectedIndex { get; set; }
        public List<MenuItemViewModel> SetEncodingMenuItems => setEncodingMenuItems;
        public List<MenuItemViewModel> SetDefaultPhonemizerMenuItems => setDefaultPhonemizerMenuItems;

        private readonly ObservableCollectionExtended<USubbank> subbanks
            = new ObservableCollectionExtended<USubbank>();
        private readonly ObservableCollectionExtended<UOto> otos
            = new ObservableCollectionExtended<UOto>();
        private readonly ReactiveCommand<Encoding, Unit> setEncodingCommand;
        private readonly List<MenuItemViewModel> setEncodingMenuItems;
        private readonly ReactiveCommand<Api.PhonemizerFactory, Unit> setDefaultPhonemizerCommand;
        private readonly List<MenuItemViewModel> setDefaultPhonemizerMenuItems;

        public SingersViewModel() {
#if DEBUG
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            if (Singers.Count() > 0) {
                Singer = Singers.First();
            }
            this.WhenAnyValue(vm => vm.Singer)
                .WhereNotNull()
                .Subscribe(singer => {
                    singer.EnsureLoaded();
                    Avatar = LoadAvatar(singer);
                    Otos.Clear();
                    Otos.AddRange(singer.Otos);
                    Info = $"Author: {singer.Author}\nVoice: {singer.Voice}\nWeb: {singer.Web}\nVersion: {singer.Version}\n{singer.OtherInfo}\n\n{string.Join("\n", singer.Errors)}";
                    LoadSubbanks();
                    DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
                });

            setEncodingCommand = ReactiveCommand.Create<Encoding>(encoding => {
                SetEncoding(encoding);
            });
            var encodings = new Encoding[] {
                Encoding.GetEncoding("shift_jis"),
                Encoding.ASCII,
                Encoding.UTF8,
                Encoding.GetEncoding("gb2312"),
                Encoding.GetEncoding("big5"),
                Encoding.GetEncoding("ks_c_5601-1987"),
                Encoding.GetEncoding("Windows-1252"),
                Encoding.GetEncoding("macintosh"),
            };
            setEncodingMenuItems = encodings.Select(encoding =>
                new MenuItemViewModel() {
                    Header = encoding.EncodingName,
                    Command = setEncodingCommand,
                    CommandParameter = encoding,
                }
            ).ToList();

            setDefaultPhonemizerCommand = ReactiveCommand.Create<Api.PhonemizerFactory>(factory => {
                SetDefaultPhonemizer(factory);
            });
            setDefaultPhonemizerMenuItems = DocManager.Inst.PhonemizerFactories.Select(factory => new MenuItemViewModel() {
                Header = factory.ToString(),
                Command = setDefaultPhonemizerCommand,
                CommandParameter = factory,
            }).ToList();
        }

        private void SetEncoding(Encoding encoding) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.TextFileEncoding = encoding.WebName);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to set encoding", e));
            }
            Refresh();
        }

        public void SetPortrait(string filepath) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.Portrait = filepath);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to set portrait", e));
            }
            Refresh();
        }

        private void SetDefaultPhonemizer(Api.PhonemizerFactory factory) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.DefaultPhonemizer = factory.type.FullName);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to set portrait", e));
            }
            Refresh();
        }

        private static void ModifyConfig(USinger singer, Action<VoicebankConfig> modify) {
            var yamlFile = Path.Combine(singer.Location, "character.yaml");
            VoicebankConfig? config = null;
            if (File.Exists(yamlFile)) {
                using (var stream = File.OpenRead(yamlFile)) {
                    config = VoicebankConfig.Load(stream);
                }
            }
            if (config == null) {
                config = new VoicebankConfig();
            }
            modify(config);
            using (var stream = File.Open(yamlFile, FileMode.Create)) {
                config.Save(stream);
            }
        }

        public void Refresh() {
            if (Singer == null) {
                return;
            }
            var singerId = Singer.Id;
            SingerManager.Inst.SearchAllSingers();
            this.RaisePropertyChanged(nameof(Singers));
            if (SingerManager.Inst.Singers.TryGetValue(singerId, out var singer)) {
                Singer = singer;
            } else {
                Singer = Singers.FirstOrDefault();
            }
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
        }

        Bitmap? LoadAvatar(USinger singer) {
            if (singer.AvatarData == null) {
                return null;
            }
            try {
                using (var stream = new MemoryStream(singer.AvatarData)) {
                    return new Bitmap(stream);
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load avatar.");
                return null;
            }
        }

        public void OpenLocation() {
            try {
                if (Singer != null) {
                    OS.OpenFolder(Singer.Location);
                }
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void LoadSubbanks() {
            Subbanks.Clear();
            if (Singer == null) {
                return;
            }
            try {
                Subbanks.AddRange(Singer.Subbanks);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to load subbanks", e));
            }
        }

        public void RefreshSinger() {
            if (Singer == null) {
                return;
            }
            int index = SelectedIndex;

            Singer.Reload();
            Avatar = LoadAvatar(Singer);
            Otos.Clear();
            Otos.AddRange(Singer.Otos);
            LoadSubbanks();

            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
            if (Otos.Count > 0) {
                index = Math.Clamp(index, 0, Otos.Count - 1);
                SelectedIndex = index;
            }
        }

        public void SetOffset(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            var delta = value - SelectedOto.Offset;
            SelectedOto.Offset += delta;
            SelectedOto.Consonant -= delta;
            SelectedOto.Preutter -= delta;
            SelectedOto.Overlap -= delta;
            if (SelectedOto.Cutoff < 0) {
                SelectedOto.Cutoff += delta;
            }
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetOverlap(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Overlap = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetPreutter(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Preutter = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetFixed(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Consonant = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetCutoff(double value, double totalDur) {
            if (SelectedOto == null || value < SelectedOto.Offset) {
                return;
            }
            SelectedOto.Cutoff = -(value - SelectedOto.Offset);
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        private static void FixCutoff(UOto oto, double totalDur) {
            double cutoff = oto.Cutoff >= 0
                ? totalDur - oto.Cutoff
                : oto.Offset - oto.Cutoff;
            // 1ms is inserted between consonant and cutoff to avoid resample problem.
            double minCutoff = oto.Offset + Math.Max(Math.Max(oto.Overlap, oto.Preutter), oto.Consonant + 1);
            if (cutoff < minCutoff) {
                oto.Cutoff = -(minCutoff - oto.Offset);
            }
        }

        public void NotifyOtoChanged() {
            if (Singer != null) {
                Singer.OtoDirty = true;
            }
            DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
        }

        public void SaveOtos() {
            if (Singer != null) {
                try {
                    Singer.Save();
                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                }
            }
            RefreshSinger();
        }

        public void GotoOto(USinger singer, UOto oto) {
            if (Singers.Contains(singer)) {
                Singer = singer;
                if (Singer.Otos.Contains(oto)) {
                    SelectedOto = oto;
                }
            }
        }
    }
}
