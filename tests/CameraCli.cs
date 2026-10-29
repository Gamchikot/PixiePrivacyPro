using System;
class CameraCli {static void Main(string[] args) {using(var camera=new Pixy())Console.WriteLine(args.Length==0?camera.Query():camera.Set(int.Parse(args[0])));}}
