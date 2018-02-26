// Copyright (c) The Avalonia Project. All rights reserved.
// Licensed under the MIT license. See licence.md file in the project root for full license information.

using Avalonia.Platform;
using SharpDX.Direct2D1;

namespace Avalonia.Direct2D1.Media
{
    /// <summary>
    /// A Direct2D implementation of a <see cref="Avalonia.Media.RectangleGeometry"/>.
    /// </summary>
    public class RectangleGeometryImpl : GeometryImpl, IRectangleGeometryImpl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamGeometryImpl"/> class.
        /// </summary>
        public RectangleGeometryImpl(Rect rect)
            : base(CreateGeometry(rect))
        {
        }

        private static Geometry CreateGeometry(Rect rect)
        {
            Factory factory = AvaloniaLocator.Current.GetService<Factory>();
            return new RectangleGeometry(factory, rect.ToDirect2D());
        }
    }
}
