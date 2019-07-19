using System;
using System.Linq;
using System.Text.RegularExpressions;
using EvDevSharp;

namespace EvTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var devices = EvDevDeviceInfo.EnumerateDevices()
                .Select(d =>
                {
                    var match = Regex.Match(d.DevicePath, @"\/dev\/input\/event([0-9]+)");
                    if (match.Success)
                        return (int.Parse(match.Groups[1].Value), d);
                    else
                        return (int.MaxValue, d);
                }).OrderBy(x => x.Item1).ThenBy(x => x.Item2).Select(x => x.Item2).ToList();
            

            void Tab(int count, string s)
            {
                for (var c = 0; c < count; c++)
                    Console.Write("    ");
                Console.WriteLine(s);
            }
            foreach (var dev in devices)
            {
                Console.WriteLine("Device " + dev.DevicePath);
                Tab(1, $"Id: {dev.Id}");
                Tab(1, $"Name: {dev.Name}");
                Tab(1, $"Guessed type: {dev.GuessedDeviceType}");
                Tab(1, "Properties: "+string.Join(",", dev.Properties));
                if (dev.RelativeAxises != null)
                    Tab(1, "Relative axises: " + string.Join(",", dev.RelativeAxises.OrderBy(x => x)));
                if (dev.AbsoluteAxises != null)
                {
                    Tab(1, "Absolute axises: ");
                    foreach (var axis in dev.AbsoluteAxises.OrderBy(x => x.Key))
                    {

                        Tab(2, "\t" + axis.Key);
                        Tab(3, $"Value: {axis.Value.Value}");
                        Tab(3, $"Min: {axis.Value.Min}");
                        Tab(3, $"Max: {axis.Value.Max}");
                        if (axis.Value.Resolution != 0)
                            Tab(3, $"Resolution: {axis.Value.Resolution}");
                        if (axis.Value.Flat != 0)
                            Tab(3, $"Flat: {axis.Value.Flat}");
                        if (axis.Value.Fuzz != 0)
                            Tab(3, $"Fuzz: {axis.Value.Fuzz}");

                    }
                }

                if (dev.Keys != null)
                {
                    Tab(1, "Keys:");
                    foreach (var k in dev.Keys)
                        Tab(2, k.ToString());
                }
            }
            
        }
    }
}