﻿using System;
using System.Runtime.CompilerServices;
using Raven.Server.Documents.Indexes.Static;
using Sparrow;
using Sparrow.Binary;
using Sparrow.Json;

namespace Raven.Server.Documents.Indexes.MapReduce
{
    public unsafe class ReduceKeyProcessor
    {
        private readonly UnmanagedBuffersPool _buffersPool;
        private readonly Mode _mode;
        private UnmanagedBuffersPool.AllocatedMemoryData _buffer;
        private int _bufferPos;
        private ulong _singleValueHash;

        public ReduceKeyProcessor(int numberOfReduceFields, UnmanagedBuffersPool buffersPool)
        {
            _buffersPool = buffersPool;
            if (numberOfReduceFields == 1)
            {
                _mode = Mode.SingleValue;
            }
            else
            {
                _mode = Mode.MultipleValues;
                _buffer = _buffersPool.Allocate(16);
                _bufferPos = 0;
            }
        }

        public void Init()
        {
            _bufferPos = 0;
        }

        public ulong Hash
        {
            get
            {
                switch (_mode)
                {
                    case Mode.SingleValue:
                        return _singleValueHash;
                    case Mode.MultipleValues:
                        return Hashing.XXHash64.CalculateInline((byte*)_buffer.Address, _bufferPos);
                    default:
                        throw new NotSupportedException($"Unknown reduce value processing mode: {_mode}");
                }
            }
        }

        public void Process(object value)
        {
            var lsv = value as LazyStringValue;
            if (lsv != null)
            {
                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate(lsv.Buffer, lsv.Size);
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer(lsv.Buffer, lsv.Size);
                        break;
                }

                return;
            }

            var s = value as string;
            if (s != null)
            {
                fixed (char* p = s)
                {
                    switch (_mode)
                    {
                        case Mode.SingleValue:
                            _singleValueHash = Hashing.XXHash64.Calculate((byte*)p, s.Length * sizeof(char));
                            break;
                        case Mode.MultipleValues:
                            CopyToBuffer((byte*)p, s.Length * sizeof(char));
                            break;
                    }
                }

                return;
            }

            var lcsv = value as LazyCompressedStringValue;
            if (lcsv != null)
            {
                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate(lcsv.Buffer, lcsv.CompressedSize);
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer(lcsv.Buffer, lcsv.CompressedSize);
                        break;
                }

                return;
            }

            if (value is long)
            {
                var l = (long)value;

                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate((byte*)&l, sizeof(long));
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer((byte*)&l, sizeof(long));
                        break;
                }

                return;
            }

            if (value is decimal)
            {
                var l = (decimal)value;

                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate((byte*)&l, sizeof(decimal));
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer((byte*)&l, sizeof(decimal));
                        break;
                }

                return;
            }

            if (value is int)
            {
                var i = (int)value;

                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate((byte*)&i, sizeof(int));
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer((byte*)&i, sizeof(int));
                        break;
                }

                return;
            }

            if (value is double)
            {
                var d = (double)value;

                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate((byte*)&d, sizeof(double));
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer((byte*)&d, sizeof(double));
                        break;
                }

                return;
            }

            var dynamicJson = value as DynamicBlittableJson;

            if (dynamicJson != null)
            {
                var obj = dynamicJson.BlittableJson;
                switch (_mode)
                {
                    case Mode.SingleValue:
                        _singleValueHash = Hashing.XXHash64.Calculate(obj.BasePointer, obj.Size);
                        break;
                    case Mode.MultipleValues:
                        CopyToBuffer(obj.BasePointer, obj.Size);
                        break;
                } 

                return;
            }

            throw new NotSupportedException($"Unhandled type: {value.GetType()}"); // TODO arek
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyToBuffer(byte* value, int size)
        {
            if (_bufferPos + size > _buffer.SizeInBytes)
            {
                var newBuffer = _buffersPool.Allocate(Bits.NextPowerOf2(_bufferPos + size));
                Memory.Copy((byte*)newBuffer.Address, (byte*)_buffer.Address, _buffer.SizeInBytes);

                _buffersPool.Return(_buffer);
                _buffer = newBuffer;
            }

            Memory.Copy((byte*)_buffer.Address + _bufferPos, value, size);
            _bufferPos += size;
        }

        enum Mode
        {
            SingleValue,
            MultipleValues
        }
    }
}