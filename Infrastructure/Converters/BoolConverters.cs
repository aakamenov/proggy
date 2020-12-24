﻿using System.Linq;
using Avalonia.Data.Converters;

namespace Proggy.Infrastructure.Converters
{
    public static class BoolConverters
    {
        public static readonly IMultiValueConverter Or =
            new FuncMultiValueConverter<bool, bool>(x => x.Any(y => y));
    }
}
