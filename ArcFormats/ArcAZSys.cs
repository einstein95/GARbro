//! \file       ArcAZSys.cs
//! \date       Wed Apr 22 09:52:23 2015
//! \brief      AZ system archive implementation.
//
// Copyright (C) 2015 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.AZSys
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AZ"; } }
        public override string Description { get { return "AZ system resource archive"; } }
        public override uint     Signature { get { return 0x1a435241; } } // 'ARC\x1a'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int ext_count = file.View.ReadInt32 (4);
            int count = file.View.ReadInt32 (8);
            uint index_length = file.View.ReadUInt32 (12);
            if (ext_count < 1 || ext_count > 8 || count <= 0 || count > 0xfffff
                || index_length <= 0x14 || index_length >= file.MaxOffset)
                return null;
            var packed_index = new byte[index_length];
            if ((int)index_length != file.View.Read (0x30, packed_index, 0, index_length))
                return null;
            uint base_offset = 0x30 + index_length;
            uint crc = LittleEndian.ToUInt32 (packed_index, 0);
            if (crc != Crc32.Compute (packed_index, 0x14, packed_index.Length-0x14))
                throw new InvalidFormatException ("CRC32 mismatch");
            var reader = new IndexReader (packed_index, count);
            var index = reader.Unpack();
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset + 0x10, 0x30);
                if (name.Length > 0)
                {
                    var entry = FormatCatalog.Instance.CreateEntry (name);
                    entry.Offset = base_offset + LittleEndian.ToUInt32 (index, index_offset);
                    entry.Size = LittleEndian.ToUInt32 (index, index_offset + 4);
                    if (entry.CheckPlacement (file.MaxOffset))
                        dir.Add (entry);
                }
                index_offset += 0x40;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        internal class IndexReader
        {
            byte[]  m_input;
            byte[]  m_output;
            int     m_control_len;
            int     m_compr1_len;
            int     m_compr2_len;
            int     m_output_len;

            public byte[] Index { get { return m_output; } }

            public IndexReader (byte[] packed, int count)
            {
                m_input = packed;
                m_output = new byte[count*0x40];
                m_control_len = LittleEndian.ToInt32 (packed, 4);
                m_compr1_len = LittleEndian.ToInt32 (packed, 8);
                m_compr2_len = LittleEndian.ToInt32 (packed, 12);
                m_output_len = LittleEndian.ToInt32 (packed, 0x10);
            }

            public byte[] Unpack ()
            {
                int control = 0x14;
                int compr1 = control + m_control_len;
                int compr2 = compr1 + m_compr1_len;
                int dst = 0;
                byte mask = 0x80;
                int copy_count;
                while (dst < m_output.Length)
                {
                    if (0 != (m_input[control] & mask))
                    {
                        int offset = LittleEndian.ToUInt16 (m_input, compr1);
                        compr1 += 2;
                        copy_count = (offset >> 13) + 3;
                        offset &= 0x1fff;
                        offset++;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, copy_count);
                        dst += copy_count;
                    }
                    else
                    {
                        copy_count = m_input[compr2++] + 1;
                        Buffer.BlockCopy (m_input, compr2, m_output, dst, copy_count);
                        compr2 += copy_count;
                        dst += copy_count;
                    }
                    mask >>= 1;
                    if (0 == mask)
                    {
                        ++control;
                        mask = 0x80;
                    }
                }
                return m_output;
            }
        }
    }
}
