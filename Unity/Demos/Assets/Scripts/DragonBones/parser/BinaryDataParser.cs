﻿using System;
using System.IO;
using System.Collections.Generic;

namespace DragonBones
{
    public class BinaryDataParser : ObjectDataParser
    {
        //JsonParse
        public delegate object JsonParseDelegate(string json);

        public static JsonParseDelegate jsonParseDelegate;

        private byte[] _binary;
        private int _binaryOffset;
        private short[] _intArrayBuffer;
        private float[] _floatArrayBuffer;
        private short[] _frameIntArrayBuffer;
        private float[] _frameFloatArrayBuffer;
        private short[] _frameArrayBuffer;
        private ushort[] _timelineArrayBuffer;

        /**
         * @private
         */
        private bool _InRange(int a, int min, int max)
        {
            return min <= a && a <= max;
        }
        /**
         * @private
         */
        private string _DecodeUTF8(ushort[] data)
        {
            var EOF_byte = -1;
            var EOF_code_point = -1;
            var FATAL_POINT = 0xFFFD;

            var pos = 0;
            var result = "";
            int? code_point;
            var utf8_code_point = 0;
            var utf8_bytes_needed = 0;
            var utf8_bytes_seen = 0;
            var utf8_lower_boundary = 0;

            while (data.Length > pos)
            {
                var _byte = data[pos++];

                if (_byte == EOF_byte)
                {
                    if (utf8_bytes_needed != 0)
                    {
                        code_point = FATAL_POINT;
                    }
                    else
                    {
                        code_point = EOF_code_point;
                    }
                }
                else
                {
                    if (utf8_bytes_needed == 0)
                    {
                        if (this._InRange(_byte, 0x00, 0x7F))
                        {
                            code_point = _byte;
                        }
                        else
                        {
                            if (this._InRange(_byte, 0xC2, 0xDF))
                            {
                                utf8_bytes_needed = 1;
                                utf8_lower_boundary = 0x80;
                                utf8_code_point = _byte - 0xC0;
                            }
                            else if (this._InRange(_byte, 0xE0, 0xEF))
                            {
                                utf8_bytes_needed = 2;
                                utf8_lower_boundary = 0x800;
                                utf8_code_point = _byte - 0xE0;
                            }
                            else if (this._InRange(_byte, 0xF0, 0xF4))
                            {
                                utf8_bytes_needed = 3;
                                utf8_lower_boundary = 0x10000;
                                utf8_code_point = _byte - 0xF0;
                            }
                            else
                            {

                            }

                            utf8_code_point = utf8_code_point * (int)Math.Pow(64, utf8_bytes_needed);
                            code_point = null;
                        }
                    }
                    else if (!this._InRange(_byte, 0x80, 0xBF))
                    {
                        utf8_code_point = 0;
                        utf8_bytes_needed = 0;
                        utf8_bytes_seen = 0;
                        utf8_lower_boundary = 0;
                        pos--;
                        code_point = _byte;
                    }
                    else
                    {
                        utf8_bytes_seen += 1;
                        utf8_code_point = utf8_code_point + (_byte - 0x80) * (int)Math.Pow(64, utf8_bytes_needed - utf8_bytes_seen);

                        if (utf8_bytes_seen != utf8_bytes_needed)
                        {
                            code_point = null;
                        }
                        else
                        {
                            var cp = utf8_code_point;
                            var lower_boundary = utf8_lower_boundary;
                            utf8_code_point = 0;
                            utf8_bytes_needed = 0;
                            utf8_bytes_seen = 0;
                            utf8_lower_boundary = 0;
                            if (this._InRange(cp, lower_boundary, 0x10FFFF) && !this._InRange(cp, 0xD800, 0xDFFF))
                            {
                                code_point = cp;
                            }
                            else
                            {
                                code_point = _byte;
                            }
                        }
                    }
                }

                //Decode string
                if (code_point != null && code_point != EOF_code_point)
                {
                    if (code_point <= 0xFFFF)
                    {
                        
                        if (code_point > 0) result += Convert.ToChar(code_point);
                    }
                    else
                    {
                        code_point -= 0x10000;
                        result += Convert.ToChar(0xD800 + ((code_point >> 10) & 0x3ff));
                        result += Convert.ToChar(0xDC00 + (code_point & 0x3ff));
                    }
                }
            }

            return result;
        }
        /**
         * @private
         */
        private TimelineData _ParseBinaryTimeline(TimelineType type, uint offset, TimelineData timelineData = null)
        {
            var timeline = timelineData != null ? timelineData : BaseObject.BorrowObject<TimelineData>();
            timeline.type = type;
            timeline.offset = offset;

            this._timeline = timeline;

            var keyFrameCount = this._timelineArrayBuffer[timeline.offset + (int)BinaryOffset.TimelineKeyFrameCount];

            if (keyFrameCount == 1)
            {
                timeline.frameIndicesOffset = -1;
            }
            else
            {
                // One more frame than animation.
                var totalFrameCount = this._animation.frameCount + 1;
                var frameIndices = this._data.frameIndices;

                timeline.frameIndicesOffset = frameIndices.Count;
                frameIndices.ResizeList(frameIndices.Count + (int)totalFrameCount);

                for (int i = 0, iK = 0, frameStart = 0, frameCount = 0; i < totalFrameCount; ++i)
                {
                    if (frameStart + frameCount <= i && iK < keyFrameCount)
                    {
                        frameStart = this._frameArrayBuffer[this._animation.frameOffset + this._timelineArrayBuffer[timeline.offset + (int)BinaryOffset.TimelineFrameOffset + iK]];
                        if (iK == keyFrameCount - 1)
                        {
                            frameCount = (int)this._animation.frameCount - frameStart;
                        }
                        else
                        {
                            frameCount = this._frameArrayBuffer[this._animation.frameOffset + this._timelineArrayBuffer[timeline.offset + (int)BinaryOffset.TimelineFrameOffset + iK + 1]] - frameStart;
                        }

                        iK++;
                    }

                    frameIndices[timeline.frameIndicesOffset + i] = (uint)(iK - 1);
                }
            }

            this._timeline = null; //

            return timeline;
        }

        /**
         * @private
         */
        protected override void _ParseMesh(Dictionary<string, object> rawData, MeshDisplayData mesh)
        {
            //mesh.offset = (int)rawData[ObjectDataParser.OFFSET];
            mesh.offset = int.Parse(rawData[ObjectDataParser.OFFSET].ToString());

            var weightOffset = this._intArrayBuffer[mesh.offset + (int)BinaryOffset.MeshWeightOffset];

            if (weightOffset >= 0)
            {
                var weight = BaseObject.BorrowObject<WeightData>();

                var vertexCount = this._intArrayBuffer[mesh.offset + (int)BinaryOffset.MeshVertexCount];
                var boneCount = this._intArrayBuffer[weightOffset + (int)BinaryOffset.WeigthBoneCount];
                weight.offset = weightOffset;
                
                for (var i = 0; i < boneCount; ++i)
                {
                    var boneIndex = this._intArrayBuffer[weightOffset + (int)BinaryOffset.WeigthBoneIndices + i];
                    weight.AddBone(this._rawBones[boneIndex]);
                }

                var boneIndicesOffset = weightOffset + (short)BinaryOffset.WeigthBoneIndices + boneCount;
                var weightCount = 0;
                for (int i = 0, l = vertexCount; i < l; ++i)
                {
                    var vertexBoneCount = this._intArrayBuffer[boneIndicesOffset++];
                    weightCount += vertexBoneCount;
                    boneIndicesOffset += vertexBoneCount;
                }

                weight.count = weightCount;
                mesh.weight = weight;
            }
        }
        /**
         * @private
         */
        protected override AnimationData _ParseAnimation(Dictionary<string, object> rawData)
        {
            var animation = BaseObject.BorrowObject<AnimationData>();
            animation.frameCount = (uint)Math.Max(ObjectDataParser._GetNumber(rawData, ObjectDataParser.DURATION, 1), 1);
            animation.playTimes = (uint)ObjectDataParser._GetNumber(rawData, ObjectDataParser.PLAY_TIMES, 1);
            animation.duration = (float)animation.frameCount / (float)this._armature.frameRate;//Must float
            animation.fadeInTime = ObjectDataParser._GetNumber(rawData, ObjectDataParser.FADE_IN_TIME, 0.0f);
            animation.scale = ObjectDataParser._GetNumber(rawData, ObjectDataParser.SCALE, 1.0f);
            animation.name = ObjectDataParser._GetString(rawData, ObjectDataParser.NAME, ObjectDataParser.DEFAULT_NAME);
            if (animation.name.Length == 0)
            {
                animation.name = ObjectDataParser.DEFAULT_NAME;
            }

            // Offsets.
            var offsets = rawData[ObjectDataParser.OFFSET] as List<object>;
            animation.frameIntOffset = uint.Parse(offsets[0].ToString());
            animation.frameFloatOffset = uint.Parse(offsets[1].ToString());
            animation.frameOffset = uint.Parse(offsets[2].ToString());

            this._animation = animation;

            if (rawData.ContainsKey(ObjectDataParser.ACTION))
            {
                animation.actionTimeline = this._ParseBinaryTimeline(TimelineType.Action, uint.Parse(rawData[ObjectDataParser.ACTION].ToString()));
            }

            if (rawData.ContainsKey(ObjectDataParser.Z_ORDER))
            {
                animation.zOrderTimeline = this._ParseBinaryTimeline(TimelineType.ZOrder, uint.Parse(rawData[ObjectDataParser.Z_ORDER].ToString()));
            }

            if (rawData.ContainsKey(ObjectDataParser.BONE))
            {
                var rawTimeliness = rawData[ObjectDataParser.BONE] as Dictionary<string, object>;
                foreach (var k in rawTimeliness.Keys)
                {
                    var rawTimelines = rawTimeliness[k] as List<object>;

                    var bone = this._armature.GetBone(k);
                    if (bone == null)
                    {
                        continue;
                    }

                    for (int i = 0, l = rawTimelines.Count; i < l; i += 2)
                    {
                        var timelineType = int.Parse(rawTimelines[i].ToString());
                        var timelineOffset = int.Parse(rawTimelines[i + 1].ToString());
                        var timeline = this._ParseBinaryTimeline((TimelineType)timelineType, (uint)timelineOffset);
                        this._animation.AddBoneTimeline(bone, timeline);
                    }
                }
            }

            if (rawData.ContainsKey(ObjectDataParser.SLOT))
            {
                var rawTimeliness = rawData[ObjectDataParser.SLOT] as Dictionary<string, object>;
                foreach (var k in rawTimeliness.Keys)
                {
                    var rawTimelines = rawTimeliness[k] as List<object>;

                    var slot = this._armature.GetSlot(k);
                    if (slot == null)
                    {
                        continue;
                    }

                    for (int i = 0, l = rawTimelines.Count; i < l; i += 2)
                    {
                        var timelineType = int.Parse(rawTimelines[i].ToString());
                        var timelineOffset = int.Parse(rawTimelines[i + 1].ToString());
                        var timeline = this._ParseBinaryTimeline((TimelineType)timelineType, (uint)timelineOffset);
                        this._animation.AddSlotTimeline(slot, timeline);
                    }
                }
            }

            this._animation = null;

            return animation;
        }
        /**
         * @private
         */
        protected override void _ParseArray(Dictionary<string, object> rawData)
        {
            var offsets = rawData[ObjectDataParser.OFFSET] as List<object>;

            int l0 = int.Parse(offsets[0].ToString());
            int l1 = int.Parse(offsets[1].ToString());
            int l2 = int.Parse(offsets[3].ToString());
            int l3 = int.Parse(offsets[5].ToString());
            int l4 = int.Parse(offsets[7].ToString());
            int l5 = int.Parse(offsets[9].ToString());
            int l6 = int.Parse(offsets[11].ToString());

            short[] intArray = { };
            float[] floatArray = { };
            short[] frameIntArray = { };
            float[] frameFloatArray = { };
            short[] frameArray = { };
            ushort[] timelineArray = { };
            
            using (MemoryStream ms = new MemoryStream(_binary))
            using (BinaryDataReader reader = new BinaryDataReader(ms))
            {
                //ToRead
                reader.Seek(this._binaryOffset, SeekOrigin.Begin);

                intArray = reader.ReadInt16s(l0, l1 / Helper.INT16_SIZE);
                floatArray = reader.ReadSingles(0, l2 / Helper.FLOAT_SIZE);
                frameIntArray = reader.ReadInt16s(0, l3 / Helper.INT16_SIZE);
                frameFloatArray = reader.ReadSingles(0, l4 / Helper.FLOAT_SIZE);
                frameArray = reader.ReadInt16s(0, l5 / Helper.INT16_SIZE);
                timelineArray = reader.ReadUInt16s(0, l6 / Helper.UINT16_SIZE);

                reader.Close();
                ms.Close();
            }

            this._data.binary = this._binary;
            this._data.intArray = this._intArrayBuffer = intArray;
            this._data.floatArray = this._floatArrayBuffer = floatArray;
            this._data.frameIntArray = this._frameIntArrayBuffer = frameIntArray;
            this._data.frameFloatArray = this._frameFloatArrayBuffer = frameFloatArray;
            this._data.frameArray = this._frameArrayBuffer = frameArray;
            this._data.timelineArray = this._timelineArrayBuffer = timelineArray;
        }
        /**
         * @inheritDoc
         */
        public override DragonBonesData ParseDragonBonesData(object rawObj, float scale = 1)
        {
            Helper.Assert(rawObj != null  && rawObj is byte[], "Data error.");

            byte[] bytes = rawObj as byte[];
            object header = null;
            using (MemoryStream ms = new MemoryStream(bytes))
            using (BinaryDataReader reader = new BinaryDataReader(ms))
            {
                ms.Position = 0;
                byte[] tag = reader.ReadBytes(8);

                byte[] array = System.Text.Encoding.ASCII.GetBytes("DBDT");

                if ( tag[0] != array[0] ||
                     tag[1] != array[1] ||
                     tag[2] != array[2] ||
                     tag[3] != array[3])
                {
                    Helper.Assert(false, "Nonsupport data.");
                    return null;
                }

                var headerLength = (int)reader.ReadUInt32();
                var headerBytes = reader.ReadBytes(headerLength);
                var headerString = System.Text.Encoding.UTF8.GetString(headerBytes);
                header = jsonParseDelegate != null ? jsonParseDelegate(headerString) : string.Empty;

                reader.Close();
                ms.Dispose();

                this._binary = bytes;
                this._binaryOffset = 8 + 4 + headerLength;
            }

            jsonParseDelegate = null;

            return base.ParseDragonBonesData(header, scale);
        }

        private string _GetUTF16Key(string value)
        {
            for (int i = 0, l = value.Length; i<l; ++i)
            {
                if (Convert.ToByte(value[i]) > 255)
                {
                    return Uri.EscapeUriString(value);
                }
            }

            return value;
        }
    }
}