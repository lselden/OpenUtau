﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using TinyPinyin;

namespace OpenUtau.Core.Enunu {
    [Phonemizer("Enunu Phonemizer", "ENUNU")]
    public class EnunuPhonemizer : Phonemizer {
        EnunuSinger singer;
        Dictionary<Note[], Phoneme[]> partResult = new Dictionary<Note[], Phoneme[]>();

        struct TimingResult {
            public string path_full_timing;
            public string path_mono_timing;
        }

        struct TimingResponse {
            public string error;
            public TimingResult result;
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer as EnunuSinger;
        }

        public override void SetUp(Note[][] notes) {
            partResult.Clear();
            if (notes.Length == 0 || singer == null || !singer.Found) {
                return;
            }
            ulong hash = HashNoteGroups(notes);
            var tmpPath = Path.Join(PathManager.Inst.CachePath, $"lab-{hash:x16}");
            var ustPath = tmpPath + ".tmp";
            var enutmpPath = tmpPath + "_enutemp";
            var scorePath = Path.Join(enutmpPath, $"score.lab");
            var timingPath = Path.Join(enutmpPath, $"timing.lab");
            var enunuNotes = NoteGroupsToEnunu(notes);
            double bpm = timeAxis.GetBpmAtTick(notes[0][0].position);
            if (!File.Exists(scorePath) || !File.Exists(timingPath)) {
                EnunuUtils.WriteUst(enunuNotes, bpm, singer, ustPath);
                var response = EnunuClient.Inst.SendRequest<TimingResponse>(new string[] { "timing", ustPath });
                if (response.error != null) {
                    throw new Exception(response.error);
                }
            }
            var noteIndexes = LabelToNoteIndex(scorePath, enunuNotes);
            var timing = ParseLabel(timingPath);
            timing.Zip(noteIndexes, (phoneme, noteIndex) => Tuple.Create(phoneme, noteIndex))
                .GroupBy(tuple => tuple.Item2)
                .ToList()
                .ForEach(g => {
                    if (g.Key >= 0) {
                        var noteGroup = notes[g.Key];
                        partResult[noteGroup] = g.Select(tu => tu.Item1).ToArray();
                    }
                });
        }

        static ulong HashNoteGroups(Note[][] notes) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    foreach (var ns in notes) {
                        foreach (var n in ns) {
                            writer.Write(n.lyric);
                            writer.Write(n.position);
                            writer.Write(n.duration);
                            writer.Write(n.tone);
                        }
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        static EnunuNote[] NoteGroupsToEnunu(Note[][] notes) {
            var result = new List<EnunuNote>();
            int position = 0;
            int index = 0;
            while (index < notes.Length) {
                if (position < notes[index][0].position) {
                    result.Add(new EnunuNote {
                        lyric = "R",
                        length = notes[index][0].position - position,
                        noteNum = 60,
                        noteIndex = -1,
                    });
                    position = notes[index][0].position;
                } else {
                    var lyric = notes[index][0].lyric;
                    if (lyric.Length > 0 && PinyinHelper.IsChinese(lyric[0])) {
                        lyric = PinyinHelper.GetPinyin(lyric).ToLowerInvariant();
                    }
                    result.Add(new EnunuNote {
                        lyric = lyric,
                        length = notes[index].Sum(n => n.duration),
                        noteNum = notes[index][0].tone,
                        noteIndex = index,
                    });
                    position += result.Last().length;
                    index++;
                }
            }
            return result.ToArray();
        }

        static int[] LabelToNoteIndex(string scorePath, EnunuNote[] enunuNotes) {
            var result = new List<int>();
            int lastPos = 0;
            int index = 0;
            var score = ParseLabel(scorePath);
            foreach (var p in score) {
                if (p.position != lastPos) {
                    index++;
                    lastPos = p.position;
                }
                result.Add(enunuNotes[index].noteIndex);
            }
            return result.ToArray();
        }

        static Phoneme[] ParseLabel(string path) {
            var phonemes = new List<Phoneme>();
            using (var reader = new StreamReader(path, Encoding.UTF8)) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var parts = line.Split();
                    if (parts.Length == 3 &&
                        long.TryParse(parts[0], out long pos) &&
                        long.TryParse(parts[1], out long end)) {
                        phonemes.Add(new Phoneme {
                            phoneme = parts[2],
                            position = (int)(pos / 1000L),
                        });
                    }
                }
            }
            return phonemes.ToArray();
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (partResult.TryGetValue(notes, out var phonemes)) {
                return new Result {
                    phonemes = phonemes.Select(p => {
                        double posMs = p.position * 0.1;
                        p.position = MsToTick(posMs) - notes[0].position;
                        return p;
                    }).ToArray(),
                };
            }
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = "error",
                    }
                },
            };
        }

        public override void CleanUp() {
            partResult.Clear();
        }
    }
}
