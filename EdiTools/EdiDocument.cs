﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EdiTools
{
    /// <summary>
    /// Represents an EDI document.
    /// </summary>
    public class EdiDocument
    {
        /// <summary>
        /// Initializes a new instance of class EdiDocument, optionally specifying separator characters.
        /// </summary>
        /// <param name="options">An EdiOptions containing separator characters to use when saving this document.</param>
        public EdiDocument(EdiOptions options = null)
        {
            Options = options == null ? new EdiOptions() : new EdiOptions(options);
            Segments = new List<EdiSegment>();
        }

        private EdiDocument(string edi, EdiOptions options)
        {
            if (options == null)
            {
                options = new EdiOptions();
                Options = options;
            }
            else
            {
                Options = new EdiOptions(options);
                options = new EdiOptions(options);
            }
            if (!options.SegmentTerminator.HasValue)
                options.SegmentTerminator = GuessSegmentTerminator(edi);
            if (!options.ElementSeparator.HasValue)
                options.ElementSeparator = GuessElementSeparator(edi);

            Segments = new List<EdiSegment>();
            string[] rawSegments = edi.Split(options.SegmentTerminator.Value);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                if (i == rawSegments.Length - 1 && (rawSegments[i] == null || rawSegments[i].Trim() == string.Empty))
                    break;
                string[] rawElements = rawSegments[i].TrimStart().Split(options.ElementSeparator.Value);
                var segment = new EdiSegment(rawElements[0]);
                for (int j = 1; j < rawElements.Length; j++)
                {
                    if (segment.Id.Equals("ISA", StringComparison.OrdinalIgnoreCase))
                    {
                        if (j == 16)
                        {
                            options.ComponentSeparator = rawElements[j][0];
                            segment.Elements.Add(new EdiElement(rawElements[j]));
                            continue;
                        }

                        if (j == 11)
                        {
                            if (string.CompareOrdinal(rawElements[12], "00402") >= 0 &&
                                !char.IsLetterOrDigit(rawElements[j][0]))
                            {
                                options.RepetitionSeparator = rawElements[j][0];
                                segment.Elements.Add(new EdiElement(rawElements[j]));
                                continue;
                            }
                            options.RepetitionSeparator = null;
                        }
                    }
                    segment.Elements.Add(rawElements[j] != string.Empty ? ParseElement(rawElements[j], options) : null);
                }
                Segments.Add(segment);
            }
        }

        private EdiDocument(XDocument xml)
        {
            Options = new EdiOptions();
            Segments = new List<EdiSegment>();
            LoadLoop(xml.Root);
        }

        /// <summary>
        /// Gets an EdiOptions containing separator characters used when loading or saving this document.
        /// </summary>
        public EdiOptions Options { get; private set; }

        /// <summary>
        /// Gets a list of segments belonging to this document.
        /// </summary>
        public IList<EdiSegment> Segments { get; private set; }

        /// <summary>
        /// Gets a list of transaction sets belonging to this document.
        /// </summary>
        public IList<EdiTransactionSet> TransactionSets
        {
            get
            {
                var transactionSets = new List<EdiTransactionSet>();
                EdiTransactionSet transactionSet = null;
                EdiSegment isa = null;
                EdiSegment gs = null;
                foreach (EdiSegment segment in Segments)
                {
                    switch (segment.Id.ToUpper())
                    {
                        case "ISA":
                            isa = segment;
                            break;
                        case "GS":
                            gs = segment;
                            break;
                        case "ST":
                            transactionSet = new EdiTransactionSet(isa, gs);
                            transactionSets.Add(transactionSet);
                            break;
                        case "GE":
                            gs = null;
                            break;
                        case "IEA":
                            isa = null;
                            break;
                    }
                    if (transactionSet == null)
                        continue;
                    transactionSet.Segments.Add(segment);
                    if (segment.Id.Equals("SE", StringComparison.OrdinalIgnoreCase))
                        transactionSet = null;
                }
                return transactionSets;
            }
        }

        /// <summary>
        /// Creates a new EdiDocument from a string, optionally specifying separator characters.
        /// </summary>
        /// <param name="edi">A string that contains EDI.</param>
        /// <param name="options">An EdiOptions containing separator characters to use when saving this document.</param>
        /// <returns>An EdiDocument populated from the string that contains EDI.</returns>
        public static EdiDocument Parse(string edi, EdiOptions options = null)
        {
            return new EdiDocument(edi, options);
        }

        /// <summary>
        /// Creates a new EdiDocument from a file, optionally specifying separator characters.
        /// </summary>
        /// <param name="fileName">A file name that references the file to load into a new EdiDocument.</param>
        /// <param name="options">An EdiOptions containing separator characters to use when saving this document.</param>
        /// <returns>An EdiDocument that contains the contents of the specified file.</returns>
        public static EdiDocument Load(string fileName, EdiOptions options = null)
        {
            string edi = File.ReadAllText(fileName);
            return new EdiDocument(edi, options);
        }

        /// <summary>
        /// Creates a new EdiDocument from a TextReader, optionally specifying separator characters.
        /// </summary>
        /// <param name="reader">A TextReader that contains the content for the EdiDocument.</param>
        /// <param name="options">An EdiOptions containing separator characters to use when saving this document.</param>
        /// <returns>An EdiDocument that contains the contents of the specified TextReader.</returns>
        public static EdiDocument Load(TextReader reader, EdiOptions options = null)
        {
            string edi = reader.ReadToEnd();
            return new EdiDocument(edi, options);
        }

        /// <summary>
        /// Creates a new EdiDocument instance by using the specified stream, optionally specifying separator characters.
        /// </summary>
        /// <param name="stream">The stream that contains the EDI data.</param>
        /// <param name="options">An EdiOptions containing separator characters to use when saving this document.</param>
        /// <returns>An EdiDocument object that reads the data that is contained in the stream.</returns>
        public static EdiDocument Load(Stream stream, EdiOptions options = null)
        {
            using (var reader = new StreamReader(stream))
            {
                string edi = reader.ReadToEnd();
                return new EdiDocument(edi, options);
            }
        }

        /// <summary>
        /// Creates a new EdiDocument from a string containing XML.
        /// </summary>
        /// <param name="text">A string that contains XML.</param>
        /// <returns>An EdiDocument populated from the string that contains XML.</returns>
        public static EdiDocument ParseXml(string text)
        {
            XDocument xml = XDocument.Parse(text);
            return new EdiDocument(xml);
        }

        /// <summary>
        /// Creates a new EdiDocument from an XDocument.
        /// </summary>
        /// <param name="xml">The XDocument that contains the EDI data.</param>
        /// <returns>An EdiDocument object that reads the data that is contained in the XDocument.</returns>
        public static EdiDocument LoadXml(XDocument xml)
        {
            return new EdiDocument(xml);
        }

        /// <summary>
        /// Creates a new EdiDocument from an XML file.
        /// </summary>
        /// <param name="fileName">A file name that references the file to load into a new EdiDocument.</param>
        /// <returns>An EdiDocument that contains the contents of the specified file.</returns>
        public static EdiDocument LoadXml(string fileName)
        {
            XDocument xml = XDocument.Load(fileName);
            return new EdiDocument(xml);
        }

        /// <summary>
        /// Creates a new EdiDocument from a TextReader.
        /// </summary>
        /// <param name="reader">A TextReader that contains the content for the EdiDocument.</param>
        /// <returns>An EdiDocument that contains the contents of the specified TextReader.</returns>
        public static EdiDocument LoadXml(TextReader reader)
        {
            XDocument xml = XDocument.Load(reader);
            return new EdiDocument(xml);
        }

        /// <summary>
        /// Creates a new EdiDocument instance by using the specified stream.
        /// </summary>
        /// <param name="stream">The stream that contains the EDI data.</param>
        /// <returns>An EdiDocument object that reads the data that is contained in the stream.</returns>
        public static EdiDocument LoadXml(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                XDocument xml = XDocument.Load(reader);
                return new EdiDocument(xml);
            }
        }

        private char GuessElementSeparator(string edi)
        {
            Match match = Regex.Match(edi, "[^A-Z0-9]", RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new Exception("Could not guess the element separator.");
            return match.Value[0];
        }

        private char GuessSegmentTerminator(string edi)
        {
            if (edi.StartsWith("ISA", StringComparison.OrdinalIgnoreCase))
                return edi[105];
            Match match = Regex.Match(edi, @"([\x00-\x1f~])\s*$");
            if (!match.Success)
                throw new Exception("Could not guess the segment terminator.");
            return match.Groups[1].Value[0];
        }

        private EdiElement ParseElement(string rawElement, EdiOptions options)
        {
            var element = new EdiElement();
            string[] repetitions = options.RepetitionSeparator.HasValue
                                       ? rawElement.Split(options.RepetitionSeparator.Value)
                                       : new[] {rawElement};
            foreach (string rawRepetition in repetitions)
            {
                if (rawRepetition != string.Empty)
                    element.Repetitions.Add(ParseRepetition(rawRepetition, options));
            }
            return element;
        }

        private EdiRepetition ParseRepetition(string rawRepetition, EdiOptions options)
        {
            var repetition = new EdiRepetition();
            string[] components = options.ComponentSeparator.HasValue
                                      ? rawRepetition.Split(options.ComponentSeparator.Value)
                                      : new[] {rawRepetition};
            foreach (string rawComponent in components)
                repetition.Components.Add(rawComponent != string.Empty ? new EdiComponent(rawComponent) : null);
            return repetition;
        }

        private void LoadLoop(XElement loop)
        {
            foreach (XElement xml in loop.Elements())
            {
                if (xml.Name.LocalName.EndsWith("loop"))
                    LoadLoop(xml);
                else
                    LoadSegment(xml);
            }
        }

        private void LoadSegment(XElement xml)
        {
            var segment = new EdiSegment(xml.Name.LocalName.ToUpper());
            foreach (XElement element in xml.Elements())
            {
                int elementIndex = GetElementIndex(element.Name.LocalName);
                if (elementIndex == -1)
                    continue;
                while (segment.Elements.Count <= elementIndex)
                    segment.Elements.Add(null);
                if (segment.Elements[elementIndex] == null)
                    segment.Elements[elementIndex] = new EdiElement();
                segment.Elements[elementIndex].Repetitions.Add(LoadRepetition(element));
            }
            Segments.Add(segment);
        }

        private int GetElementIndex(string elementName)
        {
            int position;
            if (elementName.Length < 2 ||
                !int.TryParse(elementName.Substring(elementName.Length - 2), out position))
            {
                return -1;
            }
            return position - 1;
        }

        private EdiRepetition LoadRepetition(XElement xml)
        {
            var repetition = new EdiRepetition();
            if (xml.HasElements)
            {
                foreach (XElement element in xml.Elements())
                {
                    int elementIndex = GetElementIndex(element.Name.LocalName);
                    if (elementIndex == -1)
                        continue;
                    while (repetition.Components.Count <= elementIndex)
                        repetition.Components.Add(null);
                    if (repetition.Components[elementIndex] == null)
                        repetition.Components[elementIndex] = new EdiComponent(LoadValue(element));
                }
            }
            else
                repetition.Value = LoadValue(xml);
            return repetition;
        }

        private string LoadValue(XElement xml)
        {
            string type = null;
            XAttribute typeAttribute = xml.Attribute("type");
            if (typeAttribute != null)
                type = typeAttribute.Value;

            switch (type)
            {
                case null:
                case "id":
                case "an":
                    return xml.Value;
                case "dt":
                    DateTime date;
                    return DateTime.TryParse(xml.Value, out date) ? EdiValue.Date(8, date) : xml.Value;
                case "tm":
                    DateTime time;
                    if (!DateTime.TryParse(xml.Value, out time))
                        return xml.Value;
                    int length = Regex.Replace(xml.Value, "[^0-9]", string.Empty).Length;
                    if (xml.Value[1] == ':')
                        length++;
                    return EdiValue.Time(length, time);
                case "r":
                    decimal real;
                    return decimal.TryParse(xml.Value, out real) ? EdiValue.Real(real) : xml.Value;
                default:
                    if (type == null || type.Length != 2 || type[0] != 'n' || !char.IsDigit(type[1]))
                        return xml.Value;
                    int decimals = int.Parse(type.Substring(1));
                    decimal numeric;
                    return decimal.TryParse(xml.Value, out numeric) ? EdiValue.Numeric(decimals, numeric) : xml.Value;
            }
        }

        /// <summary>
        /// Serialize this EdiDocument to a file, overwriting an existing file, if it exists.
        /// </summary>
        /// <param name="fileName">A string that contains the name of the file.</param>
        public void Save(string fileName)
        {
            using (var writer = new StreamWriter(fileName))
            {
                Save(writer);
            }
        }

        /// <summary>
        /// Outputs this EdiDocument to the specified Stream.
        /// </summary>
        /// <param name="stream">The stream to output this EdiDocument to.</param>
        public void Save(Stream stream)
        {
            using (var writer = new StreamWriter(stream))
            {
                Save(writer);
            }
        }

        /// <summary>
        /// Serialize this EdiDocument to a TextWriter.
        /// </summary>
        /// <param name="writer">A TextWriter that the EdiDocument will be written to.</param>
        public void Save(TextWriter writer)
        {
            var options = new EdiOptions(Options);
            foreach (EdiSegment segment in Segments)
            {
                if (segment.Id.Equals("ISA", StringComparison.OrdinalIgnoreCase))
                {
                    if (segment[11] != null && string.CompareOrdinal(segment[12], "00402") >= 0)
                        options.RepetitionSeparator = segment[11][0];
                    else
                        options.RepetitionSeparator = null;
                    if (segment[16] != null)
                        options.ComponentSeparator = segment[16][0];
                }
                writer.Write(segment.ToString(options));
            }
            writer.Flush();
        }

        /// <summary>
        /// Returns the EDI for this EdiDocument.
        /// </summary>
        /// <returns>A string containing the EDI.</returns>
        public override string ToString()
        {
            var buffer = new StringWriter();
            Save(buffer);
            return buffer.ToString();
        }
    }
}