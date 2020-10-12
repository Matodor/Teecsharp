using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ComponentAce.Compression.Libs.zlib;
using TeeSharp.Core;
using TeeSharp.Core.Extensions;

namespace TeeSharp.Map
{
    public class DataFile
    {
        public readonly Stream Stream;

        public readonly DataFileHeader Header;

        public readonly ReadOnlyDictionary<int, DataFileItemTypeInfo> ItemTypes;

        public readonly ReadOnlyCollection<int> ItemsOffsets;

        public readonly ReadOnlyCollection<int> DataOffsets;

        public readonly ReadOnlyCollection<int> DataSizes;

        public readonly long ItemsStartOffset;

        public readonly long DataStartOffset;

        protected Dictionary<int, object> DataItems { get; set; }
        
        public DataFile(
            Stream stream,
            DataFileHeader header,
            IEnumerable<DataFileItemTypeInfo> itemTypes,
            int[] itemsOffsets,
            int[] dataOffsets,
            int[] dataSizes,
            long itemsStartOffset,
            long dataStartOffset
        )
        {
            Stream = stream;
            Header = header;
            ItemTypes = new ReadOnlyDictionary<int, DataFileItemTypeInfo>(itemTypes.ToDictionary(info => info.Type));
            ItemsOffsets = Array.AsReadOnly(itemsOffsets);
            DataOffsets = Array.AsReadOnly(dataOffsets);
            DataSizes = Array.AsReadOnly(dataSizes);
            ItemsStartOffset = itemsStartOffset;
            DataStartOffset = dataStartOffset;
            
            DataItems = new Dictionary<int, object>();
        }

        public bool HasItemType(int type)
        {
            return ItemTypes.ContainsKey(type);
        }
        
        public DataFileItemTypeInfo GetItemType(int type)
        {
            return ItemTypes[type];
        }

        public IEnumerable<T> GetItems<T>(int type) where T : struct
        {
            var itemTypeInfo = GetItemType(type);
            for (var i = 0; i < itemTypeInfo.ItemsCount; i++)
                yield return GetItem<T>(itemTypeInfo.ItemsOffset + i, out _);
        }
        
        public T GetItem<T>(int index, out DataFileItem itemInfo) where T : struct
        {
            // TODO add external item types support from ddnet

            var offset = ItemsStartOffset + ItemsOffsets[index];
            Stream.Seek(offset, SeekOrigin.Begin);

            if (Stream.Get(out itemInfo) &&
                Stream.Get<T>(out var item))
            {
                return item;
            }
            
            throw new Exception($"Get item error at index {index}");
        }
        
        public string GetDataAsString(int index)
        {
            if (DataItems.TryGetValue(index, out var data))
                return (string) data;
            
            DataItems.Add(index, Encoding.UTF8.GetString(GetDataBuffer(index)));
            return (string) DataItems[index];
        }
        
        public T GetDataAs<T>(int index) where T : struct
        {
            if (DataItems.TryGetValue(index, out var data))
                return (T) data;
            
            var buffer = GetDataBuffer(index);
            DataItems.Add(index, buffer.Deserialize<T>());
            
            return (T) DataItems[index];
        }        
        
        public T[] GetDataAsArrayOf<T>(int index) where T : struct
        {
            if (DataItems.TryGetValue(index, out var data))
                return (T[]) data;
            
            var buffer = GetDataBuffer(index);
            DataItems.Add(index, buffer.Deserialize<T>(buffer.Length / TypeHelper<T>.Size).ToArray());
            
            return (T[]) DataItems[index];
        }

        private Span<byte> GetDataBuffer(int index)
        {
            var dataSize = index == Header.RawDataBlocks - 1
                ? Header.RawDataBlocksSize - DataOffsets[index]
                : DataOffsets[index + 1] - DataOffsets[index];
            
            using (var outMemoryStream = new MemoryStream())
            using (var outputZipStream = new ZOutputStream(outMemoryStream))
            {
                var buffer = new Span<byte>(new byte[dataSize]);

                Stream.Seek(DataStartOffset + DataOffsets[index], SeekOrigin.Begin);
                Stream.Read(buffer);

                outputZipStream.Write(buffer);
                outputZipStream.finish();

                return outMemoryStream.ToArray();
            }
        }
    }
}