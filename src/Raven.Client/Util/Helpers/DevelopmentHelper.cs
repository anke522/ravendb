﻿using System;

namespace Raven.Client.Util.Helpers
{
    public class DevelopmentTimebombException : Exception
    {
        public DevelopmentTimebombException()
        {
        }

        public DevelopmentTimebombException(string message) : base(message)
        {
        }

        public DevelopmentTimebombException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    internal static class DevelopmentHelper
    {
        private static readonly DateTime BlowupDateTime = new DateTime(2017, 6, 1);

        public static void TimeBomb()
        {
            if (SystemTime.UtcNow > BlowupDateTime)
            {
#if DEBUG
                //in case that the exception is thrown in UnobservedTaskException 
                Console.WriteLine("Development time bomb has thrown, the date is " + BlowupDateTime);
#endif           
                throw new DevelopmentTimebombException("Development time bomb, the date is " + BlowupDateTime);
            }
        }
    }
}