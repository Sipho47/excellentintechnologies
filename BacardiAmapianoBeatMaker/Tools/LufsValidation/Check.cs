using System;
using System.IO;
public static class Check {
    public static void Main(string[] args) {
        int rate=int.Parse(args[1]);
        var meter=new BacardiLufsEngine(rate,new double[] {1,1});
        using(var reader=new BinaryReader(File.OpenRead(args[0]))) {
            while(reader.BaseStream.Position<reader.BaseStream.Length) {
                int length=(int)Math.Min(2048,(reader.BaseStream.Length-reader.BaseStream.Position)/4);
                var data=new float[length];
                for(int i=0;i<length;i++) data[i]=reader.ReadSingle();
                var original=(float[])data.Clone();
                meter.Push(data,2); meter.Refresh();
                for(int i=0;i<length;i++) if(data[i]!=original[i]) throw new Exception("Audio altered");
            }
        }
        Console.WriteLine("S={0:F3} I={1:F3}",meter.ShortTerm,meter.Integrated);
        var fresh=new BacardiLufsEngine(rate,new double[] {1,1});
        fresh.Push(new float[rate*8],2); fresh.Refresh();
        if(!double.IsNaN(fresh.ShortTerm)||!double.IsNaN(fresh.Integrated)) throw new Exception("Silence/reset failed");
    }
}
