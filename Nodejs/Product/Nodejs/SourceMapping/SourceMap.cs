﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Microsoft.NodejsTools.SourceMapping
{
    /// <summary>
    /// Reads a V3 source map as documented at https://docs.google.com/document/d/1U1RGAehQwRypUTovF1KRlpiOFze0b-_2gc6fAH0KY0k/edit?hl=en_US&pli=1&pli=1
    /// </summary>
    public sealed class SourceMap
    {
        private readonly Dictionary<string, object> _mapInfo;
        private readonly LineInfo[] _lines;
        private readonly string[] _names, _sources;
        private static readonly Dictionary<char, int> _base64Mapping = BuildBase64Mapping();

        /// <summary>
        /// Index into the mappings for the starting column
        /// </summary>
        private const int SourceStartingIndex = 0;
        /// <summary>
        /// Index into the mappings for the sources list index (optional)
        /// </summary>
        private const int SourcesIndex = 1;
        /// <summary>
        /// Index into the mappings for the zero-based starting line (optional)
        /// </summary>
        private const int OriginalLineIndex = 2;
        /// <summary>
        /// Index into the mappings for the zero-based starting column (optional)
        /// </summary>
        private const int OriginalColumnIndex = 3;
        /// <summary>
        /// Index into the mappings of the names list
        /// </summary>
        private const int NamesIndex = 4;

        /// <summary>
        /// Creates a new source map from the given input.  Raises InvalidOperationException
        /// if the file is not supported or invalid.
        /// </summary>
        /// <param name="input"></param>
        internal SourceMap(TextReader input)
        {
            this._mapInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(input.ReadToEnd());
            if (this.Version != 3)
            {
                throw new NotSupportedException("Only V3 source maps are supported");
            }

            if (this._mapInfo.TryGetValue("sources", out var value))
            {
                var sourceRoot = this.SourceRoot;

                var sources = value as ArrayList;
                this._sources = sources.Cast<string>()
                    .Select(x => sourceRoot + x)
                    .ToArray();
            }
            else
            {
                this._sources = Array.Empty<string>();
            }

            if (this._mapInfo.TryGetValue("names", out value))
            {
                var names = value as ArrayList;
                this._names = names.Cast<string>().ToArray();
            }
            else
            {
                this._names = Array.Empty<string>();
            }

            var lineInfos = new List<LineInfo>();
            if (this._mapInfo.TryGetValue("mappings", out var mappingsObj) && mappingsObj is string)
            {
                var mappings = (string)mappingsObj;
                var lines = mappings.Split(';');

                // each ; separated section represents a line in the generated file
                int sourceIndex = 0, originalLine = 0, originalColumn = 0, originalName = 0;
                foreach (var line in lines)
                {
                    if (line.Length == 0)
                    {
                        lineInfos.Add(new LineInfo(new SegmentInfo[0]));
                        continue;
                    }

                    var segments = line.Split(',');

                    // each , separated section represents a segment of the line
                    var generatedColumn = 0;
                    var segmentInfos = new List<SegmentInfo>();
                    foreach (var segment in segments)
                    {
                        // each segment is Base64 VLQ encoded

                        var info = DecodeVLQ(segment);
                        if (info.Length == 0)
                        {
                            throw new InvalidOperationException("invalid data in source map, no starting column");
                        }

                        generatedColumn += info[SourceStartingIndex];

                        if (SourcesIndex < info.Length)
                        {
                            sourceIndex += info[SourcesIndex];
                        }

                        if (OriginalLineIndex < info.Length)
                        {
                            originalLine += info[OriginalLineIndex];
                        }

                        if (OriginalColumnIndex < info.Length)
                        {
                            originalColumn += info[OriginalColumnIndex];
                        }

                        if (NamesIndex < info.Length)
                        {
                            originalName += info[NamesIndex];
                        }

                        segmentInfos.Add(
                            new SegmentInfo(
                                generatedColumn,
                                sourceIndex,
                                originalLine,
                                originalColumn,
                                originalName
                            )
                        );
                    }

                    lineInfos.Add(new LineInfo(segmentInfos.ToArray()));
                }
            }
            this._lines = lineInfos.ToArray();
        }

        private const int VLQ_CONTINUATION_MASK = 0x20;
        private const int VLQ_SHIFT = 5;

        private int[] DecodeVLQ(string value)
        {
            var res = new List<int>();

            var curPos = 0;
            while (curPos < value.Length)
            {
                long intValue = 0;
                int mappedValue, shift = 0;
                do
                {
                    if (curPos == value.Length)
                    {
                        throw new InvalidOperationException("invalid data in source map, continued value doesn't continue");
                    }

                    try
                    {
                        mappedValue = _base64Mapping[value[curPos++]];
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new InvalidOperationException("invalid data in source map, base64 data out of range");
                    }

                    intValue |= (uint)((mappedValue & ~VLQ_CONTINUATION_MASK) << shift);
                    if (intValue > int.MaxValue)
                    {
                        throw new InvalidOperationException("invalid data in source map, value is outside of 32-bit range");
                    }
                    shift += VLQ_SHIFT;
                } while ((mappedValue & VLQ_CONTINUATION_MASK) != 0);

                // least significant bit is sign bit
                if ((intValue & 0x01) != 0)
                {
                    res.Add((int)-(intValue >> 1));
                }
                else
                {
                    res.Add((int)(intValue >> 1));
                }
            }
            return res.ToArray();
        }

        /// <summary>
        /// Version number of the source map.
        /// </summary>
        internal int Version => GetValue("version", -1);

        /// <summary>
        /// Filename of the generated code
        /// </summary>
        internal string File => GetValue("file", string.Empty);

        /// <summary>
        /// Provides the root for the sources to save space, automatically
        /// included in the Sources array so it's not public.
        /// </summary>
        private string SourceRoot => GetValue("sourceRoot", string.Empty);

        /// <summary>
        /// All of the filenames that were combined.
        /// </summary>
        internal ReadOnlyCollection<string> Sources => new ReadOnlyCollection<string>(this._sources);

        /// <summary>
        /// All of the variable/method names that appear in the code
        /// </summary>
        internal ReadOnlyCollection<string> Names => new ReadOnlyCollection<string>(this._names);

        /// <summary>
        /// Maps a location in the generated code into a location in the source code.
        /// </summary>
        internal bool TryMapPoint(int lineNo, int columnNo, out SourceMapInfo res)
        {
            if (lineNo < this._lines.Length)
            {
                var line = this._lines[lineNo];
                for (var i = line.Segments.Length - 1; i >= 0; i--)
                {
                    if (line.Segments[i].GeneratedColumn <= columnNo)
                    {
                        // we map to this column
                        res = new SourceMapInfo(
                            line.Segments[i].OriginalLine,
                            line.Segments[i].OriginalColumn,
                            line.Segments[i].SourceIndex < this.Sources.Count ? this.Sources[line.Segments[i].SourceIndex] : null,
                            line.Segments[i].OriginalName < this.Names.Count ? this.Names[line.Segments[i].OriginalName] : null
                        );
                        return true;
                    }
                }
                if (line.Segments.Length > 0)
                {
                    // we map to this column
                    res = new SourceMapInfo(
                        line.Segments[0].OriginalLine,
                        line.Segments[0].OriginalColumn,
                        line.Segments[0].SourceIndex < this.Sources.Count ? this.Sources[line.Segments[0].SourceIndex] : null,
                        line.Segments[0].OriginalName < this.Names.Count ? this.Names[line.Segments[0].OriginalName] : null
                    );
                    return true;
                }
            }
            res = default(SourceMapInfo);
            return false;
        }

        /// <summary>
        /// Maps a location in the generated code into a location in the source code.
        /// </summary>
        internal bool TryMapLine(int lineNo, out SourceMapInfo res)
        {
            if (lineNo < this._lines.Length)
            {
                var line = this._lines[lineNo];
                if (line.Segments.Length > 0)
                {
                    res = new SourceMapInfo(
                        line.Segments[0].OriginalLine,
                        0,
                        this.Sources[line.Segments[0].SourceIndex],
                        line.Segments[0].OriginalName < this.Names.Count ?
                            this.Names[line.Segments[0].OriginalName] :
                            null
                    );
                    return true;
                }
            }
            res = default(SourceMapInfo);
            return false;
        }

        /// <summary>
        /// Maps a location in the source code into the generated code.
        /// </summary>
        internal bool TryMapPointBack(int lineNo, int columnNo, out SourceMapInfo res)
        {
            int? firstBestLine = null, secondBestLine = null;
            for (var i = 0; i < this._lines.Length; i++)
            {
                var line = this._lines[i];
                int? originalColumn = null;
                foreach (var segment in line.Segments)
                {
                    if (segment.OriginalLine == lineNo)
                    {
                        if (segment.OriginalColumn <= columnNo)
                        {
                            originalColumn = segment.OriginalColumn;
                        }
                        else if (originalColumn != null)
                        {
                            res = new SourceMapInfo(
                                i,
                                columnNo - originalColumn.Value,
                                this.File,
                                null
                            );
                            return true;
                        }
                        else
                        {
                            // code like:
                            //      constructor(public greeting: string) { }
                            // gets compiled into:
                            //      function Greeter(greeting) {
                            //          this.greeting = greeting;
                            //      }
                            // If we're going to pick a line out of here we'd rather pick the
                            // 2nd line where we're going to hit a breakpoint.

                            if (firstBestLine == null)
                            {
                                firstBestLine = i;
                            }
                            else if (secondBestLine == null && firstBestLine.Value != i)
                            {
                                secondBestLine = i;
                            }
                        }
                    }
                    else if (segment.OriginalLine > lineNo && firstBestLine != null)
                    {
                        // not a perfect matching on column (e.g. requested 0, mapping starts at 4)
                        res = new SourceMapInfo(secondBestLine ?? firstBestLine.Value, 0, this.File, null);
                        return true;
                    }
                }
            }

            res = default(SourceMapInfo);
            return false;
        }

        private T GetValue<T>(string name, T defaultValue)
        {
            if (this._mapInfo.TryGetValue(name, out var version) && version is T)
            {
                return (T)version;
            }
            return defaultValue;
        }

        private static Dictionary<char, int> BuildBase64Mapping()
        {
            const string base64mapping = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

            var mapping = new Dictionary<char, int>();
            for (var i = 0; i < base64mapping.Length; i++)
            {
                mapping[base64mapping[i]] = i;
            }
            return mapping;
        }

        private struct LineInfo
        {
            public readonly SegmentInfo[] Segments;

            public LineInfo(SegmentInfo[] segments)
            {
                this.Segments = segments;
            }
        }

        internal struct SegmentInfo
        {
            internal readonly int GeneratedColumn;
            internal readonly int SourceIndex;
            internal readonly int OriginalLine;
            internal readonly int OriginalColumn;
            internal readonly int OriginalName;

            internal SegmentInfo(int generatedColumn, int sourceIndex, int originalLine, int originalColumn, int originalName)
            {
                this.GeneratedColumn = generatedColumn;
                this.SourceIndex = sourceIndex;
                this.OriginalLine = originalLine;
                this.OriginalColumn = originalColumn;
                this.OriginalName = originalName;
            }
        }
    }

    internal class SourceMapInfo
    {
        internal readonly int Line, Column;
        internal readonly string FileName, Name;

        internal SourceMapInfo(int line, int column, string filename, string name)
        {
            this.Line = line;
            this.Column = column;
            this.FileName = filename;
            this.Name = name;
        }
    }
}
