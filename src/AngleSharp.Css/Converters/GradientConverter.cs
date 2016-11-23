﻿namespace AngleSharp.Css.Converters
{
    using AngleSharp.Css.Dom;
    using AngleSharp.Css.Extensions;
    using AngleSharp.Css.Parser;
    using AngleSharp.Css.Values;
    using AngleSharp.Text;
    using System;
    using System.Collections.Generic;
    using System.IO;

    abstract class GradientConverter<T> : IValueConverter
        where T : struct
    {
        private readonly String _fn;
        private readonly Boolean _repeating;

        public GradientConverter(String fn, Boolean repeating)
        {
            _fn = fn;
            _repeating = repeating;
        }

        public ICssValue Convert(StringSource source)
        {
            if (source.IsFunction(_fn))
            {
                var start = source.Index;
                var initial = ConvertInitial(source);

                if (initial.HasValue)
                {
                    var current = source.SkipSpacesAndComments();

                    if (current != Symbols.Comma)
                    {
                        return null;
                    }

                    source.SkipCurrentAndSpaces();
                }
                else
                {
                    source.BackTo(start);
                }

                var stops = ToGradientStops(source);

                if (stops != null && source.Current == Symbols.RoundBracketClose)
                {
                    var gradient = CreateGradient(initial, _repeating, stops);
                    source.SkipCurrentAndSpaces();
                    return new GradientValue(gradient);
                }
            }

            return null;
        }

        protected abstract T? ConvertInitial(StringSource source);

        protected abstract IGradient CreateGradient(T? initial, Boolean repeating, GradientStop[] stops);

        private static GradientStop[] ToGradientStops(StringSource source)
        {
            var stops = new List<GradientStop>();
            
            while (!source.IsDone)
            {
                var stop = ToGradientStop(source);

                if (stop == null)
                    break;

                var current = source.SkipSpacesAndComments();
                stops.Add(stop.Value);

                if (current != Symbols.Comma)
                    break;

                source.SkipCurrentAndSpaces();
            }

            return stops.ToArray();
        }

        private static GradientStop? ToGradientStop(StringSource source)
        {
            var color = source.ToColor();
            source.SkipSpacesAndComments();
            var position = source.ToDistance();

            if (color.HasValue)
            {
                if (position.HasValue)
                {
                    return new GradientStop(color.Value, position.Value);
                }
                else
                {
                    return new GradientStop(color.Value);
                }
            }

            return null;
        }

        private sealed class GradientValue : ICssValue
        {
            private readonly IGradient _gradient;

            public GradientValue(IGradient gradient)
            {
                _gradient = gradient;
            }

            public String CssText
            {
                get { return _gradient.ToString(); }
            }

            public void ToCss(TextWriter writer, IStyleFormatter formatter)
            {
                writer.Write(CssText);
            }
        }
    }

    sealed class LinearGradientConverter : GradientConverter<LinearGradientConverter.Options>
    {
        public LinearGradientConverter(String fn, Boolean repeating)
            : base(fn, repeating)
        {
        }

        protected override Options? ConvertInitial(StringSource source)
        {
            var angle = default(Angle?);

            if (source.IsIdentifier(CssKeywords.To))
            {
                var tmp = Angle.Zero;
                source.SkipSpacesAndComments();
                var a = source.ParseIdent();
                source.SkipSpacesAndComments();
                var b = source.ParseIdent();
                var keyword = default(String);

                if (a != null && b != null)
                {
                    if (a.IsOneOf(CssKeywords.Top, CssKeywords.Bottom))
                    {
                        var t = b;
                        b = a;
                        a = t;
                    }

                    keyword = String.Concat(a, " ", b);
                }
                else if (a != null)
                {
                    keyword = a;
                }

                if (keyword != null && Map.GradientAngles.TryGetValue(keyword, out tmp))
                {
                    angle = tmp;
                }
            }
            else
            {
                angle = source.ToAngle();
            }

            if (angle.HasValue)
            {
                return new Options { Direction = angle.Value };
            }

            return null;
        }

        protected override IGradient CreateGradient(Options? initial, Boolean repeating, GradientStop[] stops)
        {
            var angle = initial?.Direction ?? Angle.Zero;
            return new LinearGradient(angle, stops, repeating);
        }

        public struct Options
        {
            public Angle Direction;
        }
    }

    sealed class RadialGradientConverter : GradientConverter<RadialGradientConverter.Options>
    {
        public RadialGradientConverter(String fn, Boolean repeating)
            : base(fn, repeating)
        {
        }

        protected override Options? ConvertInitial(StringSource source)
        {
            var circle = false;
            var center = Point.Center;
            var width = Length.Full;
            var height = Length.Full;
            var size = RadialGradient.SizeMode.None;
            var redo = false;
            var ident = source.ParseIdent();

            if (ident != null)
            {
                if (ident.Isi(CssKeywords.Circle))
                {
                    circle = true;
                    source.SkipSpacesAndComments();
                    var radius = source.ToLength();

                    if (radius.HasValue)
                    {
                        width = height = radius.Value;
                    }
                    else
                    {
                        size = ToSizeMode(source) ?? RadialGradient.SizeMode.None;
                    }

                    redo = true;
                }
                else if (ident.Isi(CssKeywords.Ellipse))
                {
                    circle = false;
                    source.SkipSpacesAndComments();
                    var el = source.ToDistance();
                    source.SkipSpacesAndComments();
                    var es = source.ToDistance();

                    if (el.HasValue && es.HasValue)
                    {
                        width = el.Value;
                        height = es.Value;
                    }
                    else if (el.HasValue != es.HasValue)
                    {
                        return null;
                    }
                    else
                    {
                        size = ToSizeMode(source) ?? RadialGradient.SizeMode.None;
                    }

                    redo = true;
                }
                else if (Map.RadialGradientSizeModes.ContainsKey(ident))
                {
                    size = ToSizeMode(source) ?? RadialGradient.SizeMode.None;
                    source.SkipSpacesAndComments();
                    ident = source.ParseIdent();

                    if (ident != null)
                    {
                        if (ident.Isi(CssKeywords.Circle))
                        {
                            circle = true;
                            redo = true;
                        }
                        else if (ident.Isi(CssKeywords.Ellipse))
                        {
                            circle = false;
                            redo = true;
                        }
                    }
                }
            }
            else
            {
                var el = source.ToDistance();
                source.SkipSpacesAndComments();
                var es = source.ToDistance();

                if (el.HasValue && es.HasValue)
                {
                    circle = false;
                    width = el.Value;
                    height = es.Value;
                }
                else if (el.HasValue)
                {
                    circle = true;
                    width = el.Value;
                }
                else
                {
                    return null;
                }

                redo = true;
            }

            if (redo)
            {
                source.SkipSpacesAndComments();
                ident = source.ParseIdent();
            }

            if (ident != null)
            {
                if (!ident.Isi(CssKeywords.At))
                {
                    return null;
                }

                source.SkipSpacesAndComments();
                var pt = source.ToPoint();

                if (!pt.HasValue)
                {
                    return null;
                }

                center = pt.Value;
            }

            return new Options
            {
                Circle = circle,
                Center = center,
                Width = width,
                Height = height,
                Size = size
            };
        }

        protected override IGradient CreateGradient(Options? initial, Boolean repeating, GradientStop[] stops)
        {
            var circle = initial?.Circle ?? true;
            var center = initial?.Center ?? Point.Center;
            var width = initial?.Width ?? Length.Full;
            var height = initial?.Height ?? Length.Full;
            var sizeMode = initial?.Size ?? RadialGradient.SizeMode.None;
            return new RadialGradient(circle, center, width, height, sizeMode, stops, repeating);
        }

        public struct Options
        {
            public Boolean Circle;
            public Point Center;
            public Length Width;
            public Length Height;
            public RadialGradient.SizeMode Size;
        }

        private static RadialGradient.SizeMode? ToSizeMode(StringSource source)
        {
            var pos = source.Index;
            var ident = source.ParseIdent();
            var result = RadialGradient.SizeMode.None;

            if (ident != null && Map.RadialGradientSizeModes.TryGetValue(ident, out result))
            {
                return result;
            }

            source.BackTo(pos);
            return null;
        }
    }
}
