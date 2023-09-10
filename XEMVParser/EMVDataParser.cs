using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XEMVParser
{
    public class EMVDataParser
    {
        public class EMVData
        {
            public string DGI { get; set; }
            public string Tag { get; set; }

            public string
                Description { get; set; } // You would need to add logic to retrieve the description based on the tag

            public string Value { get; set; }
        }

        public List<EMVData> ParseEMVData(string data)
        {
            List<EMVData> emvDataList = new List<EMVData>();

            int index = 0;
            while (index < data.Length)
            {
                string dgi = data.Substring(index, 4);
                index += 4;

                int length = Convert.ToInt32(data.Substring(index, 4), 16);
                index += 4;

                ParseEFDirectory(data, ref index, dgi, length, emvDataList);
            }

            return emvDataList;
        }

        private void ParseEFDirectory(string data, ref int index, string dgi, int length, List<EMVData> emvDataList)
        {
            int endIndex = index + length * 2;
            while (index < endIndex)
            {
                string tag = data.Substring(index, 2);
                index += 2;

                if (tag == "61" || tag == "70" || tag == "A5")
                {
                    int nestedLength = Convert.ToInt32(data.Substring(index, 2), 16);
                    index += 2;

                    ParseEFDirectory(data, ref index, dgi, nestedLength, emvDataList);
                }
                else
                {
                    int tagLength = Convert.ToInt32(data.Substring(index, 2), 16);
                    index += 2;

                    string value = data.Substring(index, tagLength * 2);
                    index += tagLength * 2;

                    emvDataList.Add(new EMVData
                    {
                        DGI = dgi,
                        Tag = tag,
                        Description = "", // Add logic to retrieve description based on the tag
                        Value = value
                    });
                }
            }
        }

    }
}