﻿/*
    (c) 2018 tevador <tevador@gmail.com>

    This file is part of Tevador.RandomJS.

    Tevador.RandomJS is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Tevador.RandomJS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Tevador.RandomJS.  If not, see<http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using System.Net;
using Tevador.RandomJS.Statements;

namespace Tevador.RandomJS
{
    class Program : Block, IProgram
    {
        private Dictionary<string, Global> _globalNames = new Dictionary<string, Global>();
        private List<Global> _globals = new List<Global>();
        private byte[] _source;

        public Program()
            : base(null)
        {

        }

        internal List<Variable> PrintOrder
        {
            get;
            set;
        }

        public override void Require(Global gl)
        {
            if (!_globalNames.ContainsKey(gl.Name))
            {
                _globalNames.Add(gl.Name, gl);
                var gfunc = gl as GlobalFunction;
                if (gfunc?.References != null)
                {
                    Require(gfunc.References);
                }
                _globals.Add(gl);
            }
        }

        internal void SetGlobalVariable<T>(string name, T value)
        {
            Global g;
            if (value != null && _globalNames.TryGetValue(name, out g))
            {
                GlobalVariable glVar = g as GlobalVariable;
                if (glVar != null)
                {
                    glVar.Initializer = new Expressions.Literal(value.ToString());
                }
            }
        }

        public byte[] Source
        {
            get
            {
                if (_source == null)
                {
                    using (var ms = new MemoryStream(20 * 1024))
                    {
                        using (var writer = new StreamWriter(ms) { NewLine = "\n" })
                        {
                            WriteTo(writer);
                        }
                        _source = ms.ToArray();
                    }
                }
                return _source;
            }
        }

        public override void WriteTo(System.IO.TextWriter w)
        {
            w.WriteLine("'use strict';");
            w.WriteLine("/* This program was generated by Tevador.RandomJS */");
            w.WriteLine("/* Seed: {0} */", BinaryUtils.ByteArrayToString(Seed));
            w.WriteLine("/* Print order: {0} */", string.Join(", ", PrintOrder));
            foreach (var gf in _globals)
            {
                gf.WriteTo(w);
            }
            base.WriteTo(w);
        }

        // 4,86236203379662 ms per seed
        // Runtime: Min: 2,1137; Max: 22,169; Avg: 3,90796864137288; Stdev: 1,87134841989214;
        public int Execute(out string output, out string error)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://localhost:18111");
                request.KeepAlive = false;
                request.Timeout = 10000;
                request.Method = "POST";
                byte[] data = Source;
                request.ContentLength = data.Length;
                Stream reqStream = request.GetRequestStream();
                reqStream.Write(data, 0, data.Length);
                reqStream.Close();
                WebResponse response = request.GetResponse();
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    output = reader.ReadToEnd();
                }
                error = null;
                return 0;
            }
            catch(WebException e)
            {             
                output = null;
                var response = e.Response as HttpWebResponse;
                if (response != null)
                {
                    error = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    return (int)response.StatusCode;
                }
                else
                {
                    error = e.Message;
                    return e.HResult;
                }
            }
        }

        internal byte[] Seed
        {
            get; set;
        }

        static unsafe byte[] GenerateSeed(int init)
        {
            ulong smallSeed = (ulong)init;
            byte[] bigSeed = new byte[4 * sizeof(ulong)];
            fixed (byte* buffer = bigSeed)
            {
                ulong* s = (ulong*)buffer;
                s[0] = Xoshiro256Plus.SplitMix64(ref smallSeed);
                s[1] = Xoshiro256Plus.SplitMix64(ref smallSeed);
                s[2] = Xoshiro256Plus.SplitMix64(ref smallSeed);
                s[3] = Xoshiro256Plus.SplitMix64(ref smallSeed);
            }
            return bigSeed;
        }

        class RuntimeInfo : IComparable<RuntimeInfo>
        {
            public RuntimeInfo(string seed, double runtime)
            {
                Seed = seed;
                Runtime = runtime;
            }

            public string Seed { get; private set; }
            public double Runtime { get; private set; }

            public int CompareTo(RuntimeInfo other)
            {
                return Runtime.CompareTo(other.Runtime);
            }
        }

        static void MakeStats(int count)
        {
            var runtimes = new List<RuntimeInfo>(count);
            var factory = new ProgramFactory();
            string output, error;
            int exitCode = 0;
            IProgram p;
            //warm up
            p = factory.GenProgram(GenerateSeed(Environment.TickCount));
            p.Execute(out output, out error);
            Console.WriteLine("Warmed up");
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; ++i)
            {
                var seed = Environment.TickCount + i;
                var gs = GenerateSeed(seed);
                var ss = BinaryUtils.ByteArrayToString(gs);
                if ((i & 127) == 0)
                    Console.WriteLine("Seed = {0}", ss);
                p = factory.GenProgram(gs);
                exitCode = p.Execute(out output, out error);
                if (exitCode != 0)
                {
                    Console.WriteLine("Error Seed = {0}", ss);
                    Console.WriteLine($"// ExitCode = {exitCode}");
                    Console.WriteLine(output);
                    Console.WriteLine(error);
                    break;
                }
                const string prefix = "RUNTIME: ";
                int runtimeIndex = output.IndexOf(prefix);
                if(runtimeIndex < 0)
                {
                    Console.WriteLine("Runtime info not found");
                    break;
                }
                string runtimeStr = output.Substring(runtimeIndex + prefix.Length);
                double runtime;
                if(!double.TryParse(runtimeStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out runtime))
                {
                    Console.WriteLine($"Invalid Runtime info {runtimeStr}");
                    break;
                }
                runtimes.Add(new RuntimeInfo(ss, 1000 * runtime));
            }
            sw.Stop();

            Console.WriteLine($"// {runtimes.Count} seeds processed in {sw.Elapsed.TotalSeconds} seconds");

            if (exitCode != 0) return;

            runtimes.Sort();
            //runtimes.RemoveAt(0);
            //runtimes.RemoveAt(runtimes.Count - 1);
            var avg = runtimes.Average(r => r.Runtime);
            var min = runtimes[0].Runtime;
            var max = runtimes[runtimes.Count - 1].Runtime;
            Console.WriteLine($"Longest runtimes:");
            for(int i = 1; i <= 10; ++i)
            {
                var r = runtimes[runtimes.Count - i];
                Console.WriteLine($"Seed = {r.Seed}, Runtime = {r.Runtime} ms");
            }
            var sqsum = runtimes.Sum(d => (d.Runtime - avg) * (d.Runtime - avg));
            var stdev = Math.Sqrt(sqsum / runtimes.Count);
            Console.WriteLine($"Runtime: Min: {min}; Max: {max}; Avg: {avg}; Stdev: {stdev};");
            int[] histogram = new int[(int)Math.Ceiling((max - min) / stdev * 10)];
            foreach (var run in runtimes)
            {
                histogram[(int)(((run.Runtime - min) / stdev * 10))]++;
            }
            Console.WriteLine("Histogram:");
            for (int j = 0; j < histogram.Length; ++j)
            {
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1}", j * stdev / 10 + min, histogram[j]));
            }
        }

        static void Main(string[] args)
        {
            /*int count;
            if (args.Length == 0 || !int.TryParse(args[0], out count))
                count = 10000;
            MakeStats(count);
            return;*/
            byte[] seed;
            if (args.Length > 0)
            {
                string hexSeed = args[0];
                if (hexSeed.Length != 64 || hexSeed.Any(c => !"0123456789abcdef".Contains(c)))
                {
                    Console.WriteLine("Invalid seed value (expected 64 hex characters)");
                    return;
                }
                seed = BinaryUtils.StringToByteArray(hexSeed);
            }
            else
            {
                seed = GenerateSeed(Environment.TickCount);
            }
            var random = new Xoshiro256Plus();
            var p = new ProgramFactory(random).GenProgram(seed);
            p.WriteTo(Console.Out);
            Console.WriteLine($"// {random.Counter} random numbers used");
        }
    }
}
