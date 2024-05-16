using CoreLibrary;
using System.Text.RegularExpressions;

namespace PDFTextractPOAnalyzer.Core
{
    public static class AddressSplitter
    {
        private static string originalAddress;
        private static Regex pattern;

        public static void SetAddress(string a)
        {
            originalAddress = a;
            pattern = new Regex(@"^(((?<PropertyType>[a-z\ ,\.']+?)\ *?)?
                                        ((?<Unit>\d+-\d+|\d+)(,|/|-|[\ ]*?))?
                                        (\b(?<Number>\d+[a-z]?)\b)\ *?
                                        (?<Street>[\w\ '-]+)
                                        (\b(?<StreetType>STREET|ST|ROAD|RD|GROVE|GR|DRIVE|DR|AVENUE|AVE|CIRCUIT|CCT|CLOSE|CL|COURT|CRT|CT|CRESCENT|CRES|PLACE|PL|PARADE|PDE|BOULEVARD|BLVD|HIGHWAY|HWY|ALLEY|ALLY|APPROACH|APP|ARCADE|ARC|BROW|BYPASS|BYPA|CAUSEWAY|CWAY|CIRCUS|CIRC|COPSE|CPSE|CORNER|CNR|COVE|END|ESPLANANDE|ESP|FLAT|FREEWAY|FWAY|FRONTAGE|FRNT|GARDENS|GDNS|GLADE|GLD|GLEN|GREEN|GRN|HEIGHTS|HTS|LANE|LINK|LOOP|MALL|MEWS|PACKET|PCKT|PARK|PARKWAY|PKWY|PROMENADE|PROM|RESERVE|RES|RIDGE|RDGE|RISE|ROW|SQUARE|SQ|STRIP|STRP|TARN|TERRACE|TCE|THOROUGHFARE|TFRE|TRACK|TRAC|TRUNKWAY|TWAY|VIEW|VISTA|VSTA|WALK|WAY|WALKWAY|WWAY|YARD)\b).?,?\ *?
                                     )
                                     ((?<Suburb>[a-z'.]+([\-,\ ]+[a-z'.]+)*?),?\ *?)?
                                     (\b(?<State>New\ South\ Wales|NSW|Victoria|VIC|Queensland|QLD|Australian\ Capital\ Territory|ACT|South\ Australia|SA|West\ Australia|WA|Tasmania|TAS|Northern\ Territory|NT)\b,?\ *?)?
                                     ((?<Postcode>\d{4}),?\ *?)?
                                     (Au(s(tralia)?)?)?
                                     (\s(?=[^$]))* 
                                    $"
                                    , RegexOptions.IgnoreCase |
                                      RegexOptions.ExplicitCapture |
                                      RegexOptions.IgnorePatternWhitespace);
        }

        public static Address GetAddress()
        {
            Address address = new Address();
            if (!string.IsNullOrWhiteSpace(originalAddress))
            {
                //First remove any trailing /
                string tempOriginalAddress = originalAddress.Replace('/', ' ');
                Match match = pattern.Match(tempOriginalAddress);



                string[] streetArr = originalAddress.Split(match.Groups[6].ValueSpan.ToString());
                address.StreetAddress = streetArr[0];
                address.Suburb = match.Groups[6].ValueSpan.ToString();
                address.State = match.Groups[7].ValueSpan.ToString();
                address.PostCode = match.Groups[8].ValueSpan.ToString();
            }
            else
            {
                address.StreetAddress = originalAddress;
                address.Error = "Address not found";
            }

            return address;
        }
    }
}
