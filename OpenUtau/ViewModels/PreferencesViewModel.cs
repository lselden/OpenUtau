﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.Audio;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class PreferencesViewModel : ViewModelBase {
        public List<AudioOutputDevice>? AudioOutputDevices {
            get => audioOutputDevices;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevices, value);
        }
        public AudioOutputDevice? AudioOutputDevice {
            get => audioOutputDevice;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevice, value);
        }
        [Reactive] public int PreferPortAudio { get; set; }
        [Reactive] public int PlaybackAutoScroll { get; set; }
        [Reactive] public double PlayPosMarkerMargin { get; set; }
        [Reactive] public int LockStartTime { get; set; }
        public string AdditionalSingersPath => PathManager.Inst.AdditionalSingersPath;
        [Reactive] public int InstallToAdditionalSingersPath { get; set; }
        [Reactive] public int PreRender { get; set; }
        [Reactive] public int Theme { get; set; }
        [Reactive] public int ShowPortrait { get; set; }
        public List<CultureInfo>? Languages { get; }
        public CultureInfo? Language {
            get => language;
            set => this.RaiseAndSetIfChanged(ref language, value);
        }

        private List<AudioOutputDevice>? audioOutputDevices;
        private AudioOutputDevice? audioOutputDevice;
        private CultureInfo? language;

        public PreferencesViewModel() {
            var audioOutput = PlaybackManager.Inst.AudioOutput;
            if (audioOutput != null) {
                AudioOutputDevices = audioOutput.GetOutputDevices();
                int deviceNumber = audioOutput.DeviceNumber;
                var device = AudioOutputDevices.FirstOrDefault(d => d.deviceNumber == deviceNumber);
                if (device != null) {
                    AudioOutputDevice = device;
                }
            }
            PreferPortAudio = Preferences.Default.PreferPortAudio ? 1 : 0;
            PlaybackAutoScroll = Preferences.Default.PlaybackAutoScroll;
            PlayPosMarkerMargin = Preferences.Default.PlayPosMarkerMargin;
            LockStartTime = Preferences.Default.LockStartTime;
            InstallToAdditionalSingersPath = Preferences.Default.InstallToAdditionalSingersPath ? 1 : 0;
            ToolsManager.Inst.Initialize();
            var pattern = new Regex(@"Strings\.([\w-]+)\.axaml");
            Languages = Application.Current.Resources.MergedDictionaries
                .Select(res => (ResourceInclude)res)
                .OfType<ResourceInclude>()
                .Select(res => pattern.Match(res.Source!.OriginalString))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .Select(lang => CultureInfo.GetCultureInfo(lang))
                .ToList();
            Languages.Insert(0, CultureInfo.GetCultureInfo("en-US"));
            Languages.Insert(0, null);
            Language = string.IsNullOrEmpty(Preferences.Default.Language)
                ? null
                : CultureInfo.GetCultureInfo(Preferences.Default.Language);
            PreRender = Preferences.Default.PreRender ? 1 : 0;
            Theme = Preferences.Default.Theme;
            ShowPortrait = Preferences.Default.ShowPortrait ? 1 : 0;

            this.WhenAnyValue(vm => vm.AudioOutputDevice)
                .WhereNotNull()
                .SubscribeOn(RxApp.MainThreadScheduler)
                .Subscribe(device => {
                    if (PlaybackManager.Inst.AudioOutput != null) {
                        try {
                            PlaybackManager.Inst.AudioOutput.SelectDevice(device.guid, device.deviceNumber);
                        } catch (Exception e) {
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"Failed to select device {device.name}", e));
                        }
                    }
                });
            this.WhenAnyValue(vm => vm.PreferPortAudio)
                .Subscribe(index => {
                    Preferences.Default.PreferPortAudio = index > 0;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PlaybackAutoScroll)
                .Subscribe(autoScroll => {
                    Preferences.Default.PlaybackAutoScroll = autoScroll;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PlayPosMarkerMargin)
                .Subscribe(playPosMarkerMargin => {
                    Preferences.Default.PlayPosMarkerMargin = playPosMarkerMargin;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.LockStartTime)
                .Subscribe(lockStartTime => {
                    Preferences.Default.LockStartTime = lockStartTime;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.InstallToAdditionalSingersPath)
                .Subscribe(index => {
                    Preferences.Default.InstallToAdditionalSingersPath = index > 0;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PreRender)
                .Subscribe(preRender => {
                    Preferences.Default.PreRender = preRender > 0;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.Language)
                .Subscribe(lang => {
                    Preferences.Default.Language = lang?.Name ?? string.Empty;
                    Preferences.Save();
                    App.SetLanguage(Preferences.Default.Language);
                });
            this.WhenAnyValue(vm => vm.Theme)
                .Subscribe(theme => {
                    Preferences.Default.Theme = theme;
                    Preferences.Save();
                    App.SetTheme();
                });
            this.WhenAnyValue(vm => vm.ShowPortrait)
                .Subscribe(index => {
                    Preferences.Default.ShowPortrait = index > 0;
                    Preferences.Save();
                });
        }

        public void TestAudioOutputDevice() {
            PlaybackManager.Inst.PlayTestSound();
        }

        public void OpenResamplerLocation() {
            try {
                string path = PathManager.Inst.ResamplersPath;
                Directory.CreateDirectory(path);
                OS.OpenFolder(path);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void SetAddlSingersPath(string path) {
            Preferences.Default.AdditionalSingerPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(AdditionalSingersPath));
        }
    }
}
