namespace Sharlayan.Resources {
    using System;
    using System.Collections.Generic;

    using Sharlayan.Models;
    using Sharlayan.Models.Resources;
    using Sharlayan.Models.Structures;
    using ChatLogPointersStructure = Sharlayan.Models.Structures.ChatLogPointers;

    internal sealed class HermesMappedResources {
        internal HermesMappedResources(Signature[] signatures, StructuresContainer structures, TalkMemoryLayout talkLayout) {
            this.Signatures = signatures;
            this.Structures = structures;
            this.TalkLayout = talkLayout;
        }

        internal Signature[] Signatures { get; }
        internal StructuresContainer Structures { get; }
        internal TalkMemoryLayout TalkLayout { get; }
    }

    internal sealed class TalkMemoryLayout {
        internal TalkMemoryLayout(int stringPointerOffset, int bufferUsedOffset, int stringLengthOffset) {
            this.StringPointerOffset = stringPointerOffset;
            this.BufferUsedOffset = bufferUsedOffset;
            this.StringLengthOffset = stringLengthOffset;
            this.HeaderSize = Math.Max(stringLengthOffset + 8, Math.Max(stringPointerOffset + 8, bufferUsedOffset + 8));
        }

        internal int StringPointerOffset { get; }
        internal int BufferUsedOffset { get; }
        internal int StringLengthOffset { get; }
        internal int HeaderSize { get; }
    }

    internal static class HermesV2ResourceMapper {
        internal static HermesMappedResources Map(HermesV2Manifest manifest) {
            HermesFrameworkRoot root = manifest.Roots.Framework;
            HermesChatLogResource chat = manifest.Resources.ChatLog;
            HermesTalkResource talk = manifest.Resources.Talk;
            int patternLength = root.Pattern.Length / 2;
            long rewindOffset = -(patternLength - root.RelativeFollowOffset);

            Signature[] signatures = {
                CreateSignature(Signatures.CHATLOG_KEY, root.Pattern, rewindOffset, chat.UiModuleOffset, chat.RaptureLogModuleOffset),
                CreateSignature(Signatures.LAST_TALK_NAME_KEY, root.Pattern, rewindOffset, talk.UiModuleOffset, talk.NameOffset),
                CreateSignature(Signatures.LAST_TALK_TEXT_KEY, root.Pattern, rewindOffset, talk.UiModuleOffset, talk.TextOffset),
            };
            StructuresContainer structures = new StructuresContainer {
                ChatLogPointers = new ChatLogPointersStructure {
                    OffsetArrayStart = chat.IndexVectorOffset,
                    OffsetArrayPos = chat.IndexVectorOffset + 8,
                    OffsetArrayEnd = chat.IndexVectorOffset + 16,
                    LogStart = chat.DataVectorOffset,
                    LogNext = chat.DataVectorOffset + 8,
                    LogEnd = chat.DataVectorOffset + 16,
                },
            };
            TalkMemoryLayout layout = new TalkMemoryLayout(
                talk.Utf8String.StringPointerOffset,
                talk.Utf8String.BufferUsedOffset,
                talk.Utf8String.StringLengthOffset);
            return new HermesMappedResources(signatures, structures, layout);
        }

        private static Signature CreateSignature(string key, string pattern, long rewindOffset, int uiModuleOffset, int resourceOffset) {
            return new Signature {
                ASMSignature = true,
                Key = key,
                PointerPath = new List<long> { rewindOffset, 0, uiModuleOffset, resourceOffset },
                Value = pattern,
            };
        }
    }
}
