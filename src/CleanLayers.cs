using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace DieYing
{
    internal sealed partial class LayeredCharacterRig
    {
        private Layer HuangPart(Bitmap sheet,Rectangle source,RectangleF destination,int kind,PointF pivot)
        {return Cut(sheet,source,H(destination),kind,H(pivot.X,pivot.Y));}
        private void LoadCleanHuangParts(Costume c,string directory,bool stage)
        {
            using(var sheet=new Bitmap(Path.Combine(directory,(stage?"stage":"classic")+"-limbs.png")))
            {
                if(stage)
                {
                    c.arms[0]=HuangPart(sheet,new Rectangle(137,555,551,338),new RectangleF(347,595,320,200),7,new PointF(644,618));
                    c.arms[1]=HuangPart(sheet,new Rectangle(878,555,523,337),new RectangleF(850,595,307,200),8,new PointF(866,618));
                    c.brows[0]=HuangPart(sheet,new Rectangle(558,313,161,64),new RectangleF(610,383,121,45),6,new PointF(670,412));
                    c.brows[1]=HuangPart(sheet,new Rectangle(817,313,164,66),new RectangleF(780,395,126,38),6,new PointF(840,417));
                }
                else
                {
                    c.arms[0]=HuangPart(sheet,new Rectangle(232,573,412,282),new RectangleF(313,595,323,229),7,new PointF(621,614));
                    c.arms[1]=HuangPart(sheet,new Rectangle(887,574,412,257),new RectangleF(832,605,305,179),8,new PointF(845,619));
                    c.brows[0]=HuangPart(sheet,new Rectangle(548,326,131,52),new RectangleF(539,390,139,38),6,new PointF(610,412));
                    c.brows[1]=HuangPart(sheet,new Rectangle(767,327,133,50),new RectangleF(723,386,140,44),6,new PointF(790,412));
                }
            }
        }
        private void LoadCleanZhuParts(Costume c,string directory,bool stage)
        {
            using(var sheet=new Bitmap(Path.Combine(directory,stage?"stage-limbs.png":"casual-parts.png")))
            {
                c.arms[0]=Cut(sheet,stage?new Rectangle(215,654,369,305):new Rectangle(95,657,383,290),new RectangleF(349,719,234,205),7,new PointF(563,737));
                c.arms[1]=Cut(sheet,stage?new Rectangle(853,653,351,250):new Rectangle(842,657,385,290),stage?new RectangleF(761,718,239,158):new RectangleF(774,711,239,205),8,new PointF(804,737));
            }
        }
        private static GraphicsPath HuangEyeMask(bool stage)
        {
            var path=new GraphicsPath();
            PointF[][] shapes=stage?new PointF[][]{
                new[]{new PointF(526,418),new PointF(568,402),new PointF(604,397),new PointF(667,423),new PointF(725,446),new PointF(740,477),new PointF(730,553),new PointF(558,553),new PointF(529,503)},
                new[]{new PointF(778,467),new PointF(806,443),new PointF(872,433),new PointF(928,431),new PointF(993,456),new PointF(1004,523),new PointF(945,563),new PointF(790,563)}}:
                new PointF[][]{
                new[]{new PointF(479,424),new PointF(514,404),new PointF(558,407),new PointF(615,424),new PointF(674,449),new PointF(689,489),new PointF(670,540),new PointF(509,548),new PointF(477,501)},
                new[]{new PointF(733,467),new PointF(780,433),new PointF(855,411),new PointF(911,425),new PointF(956,460),new PointF(953,516),new PointF(917,546),new PointF(738,541)}};
            foreach(var shape in shapes){for(int i=0;i<shape.Length;i++)shape[i]=H(shape[i].X,shape[i].Y);path.AddPolygon(shape);}
            return path;
        }
    }
}
