using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UIAutoLib.Helpers
{
    public static class AutomationExtensions
    {
        public static IEnumerable<AutomationElement> FindAllAncestors(this AutomationElement element)
        {
            var current = element.Parent;
            while (current != null)
            {
                yield return current;
                current = current.Parent;
            }
        }

        public static AutomationElement FindVisualParent(AutomationElement target, IEnumerable<AutomationElement> allElements, int index)
        {
            var targetRect = target.BoundingRectangle;

            var candidates = allElements
                .Where(e =>
                    e != target &&                   
                    IsInside(e.BoundingRectangle, targetRect)
                )
                .OrderBy(e => e.BoundingRectangle.Width * e.BoundingRectangle.Height)
                .ToArray();

            return candidates[index];
        }
        public static bool IsInside(RectangleF outer, RectangleF inner, float tolerance = 2)
        {
            return
                inner.Left >= outer.Left - tolerance &&
                inner.Top >= outer.Top - tolerance &&
                inner.Right <= outer.Right + tolerance &&
                inner.Bottom <= outer.Bottom + tolerance;
        }
    }
}
