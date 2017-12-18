#r "./tessnet2_32.dll"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Data"
#r "System.Drawing"

using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

public class kNNEntity
{
    public int[] array;
    public string font;

    public kNNEntity(int[] array, string font)
    {
        this.array = array;
        this.font = font;
    }
}

public static void Run(ICloudBlob myBlob, TraceWriter log, out object outputRecord)
{
    log.Info($"C# Blob trigger function processed: {myBlob.Name}");
    
    string finalString;
    Dictionary<string, List<kNNEntity>> kNNGroups = new Dictionary<string, List<kNNEntity>>();
    Dictionary<string, double> UltimateResult = new Dictionary<string, double>();
    int k = 17;

    // Create an image object from a stream
    MemoryStream ms = new MemoryStream();
    myBlob.DownloadToStream(ms);
    Bitmap image = new Bitmap(ms);
    
    log.Info($"Image loaded");

    // Load kNN training data
    kNNGroups = JsonConvert.DeserializeObject<Dictionary<string, List<kNNEntity>>>
                (File.ReadAllText(@"D:\home\site\wwwroot\BlobTriggerProcessImg\kNNdata\TrainElements_dim30.json"));
    log.Info($"kNN data loaded");
    
    tessnet2.Tesseract ocr = new tessnet2.Tesseract();
    log.Info($"Tesseract loaded");
    
    ocr.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");
    log.Info($"Tesseract variables set");
    
    ocr.Init(@"D:\home\site\wwwroot\BlobTriggerProcessImg\tessdata","eng", false); // To use correct tessdata
    log.Info($"Tesseract tessdata set");
    
    List<tessnet2.Word> OCRresult = ocr.DoOCR(image, Rectangle.Empty);
    string resultString = "";
    
    Dictionary<string, List<Rectangle>> CharLocations = new Dictionary<string, List<Rectangle>>();
    int charCount = 0;
    
    foreach (tessnet2.Word word in OCRresult)
    {
        log.Info($"{word}");
        resultString += word.Text + " ";
        
        foreach (tessnet2.Character character in word.CharList)
        {
            charCount++;
            log.Info($"{character.Value} : {FindCharLocation(character.Left, character.Right, character.Top, character.Bottom)}");
            
            Rectangle charPosition = FindCharLocation(character.Left, character.Right, character.Top, character.Bottom);
            
            List<Rectangle> allCharBounds;
            if (!CharLocations.TryGetValue(character.Value.ToString(), out allCharBounds))
            {
                allCharBounds = new List<Rectangle>();
                CharLocations.Add(character.Value.ToString(), allCharBounds);
            }
            allCharBounds.Add(charPosition);
            //log.Info($"{CharLocations.Count()}");
        }
    }

    foreach (var charPositions in CharLocations)
    {  
        foreach (var charLocation in charPositions.Value)
        {
            using (Bitmap croppedImage = ScaleImage(image.Clone(charLocation, image.PixelFormat), 30, 30))
            {
                var tempDict = FindkNN(k, kNNGroups, croppedImage, charPositions.Key);
                
                foreach (var result in tempDict)
                {
                    if (!UltimateResult.ContainsKey(result.Key))
                        UltimateResult.Add(result.Key, result.Value);
                    else
                        UltimateResult[result.Key] += result.Value;
                    //Console.WriteLine(result.Key + ": " + result.Value);
                }

            }
        }
    }
    
    //Console.WriteLine(res.Key + ": " + Math.Round(res.Value / CharLocations.Count(), 3) * 100);
    finalString = string.Join(";", UltimateResult.OrderByDescending(key => key.Value).Select(x => x.Key + ": " + 
        Math.Round(x.Value / CharLocations.Count(), 3) * 100));

    log.Info($"Final result: {finalString}");
    log.Info($"Done.");
    
    outputRecord = new 
    {
        TimeStamp = DateTime.UtcNow,
        fontResult = finalString,
        textResult = resultString,
        imageURL = myBlob.Uri.ToString(),
        charCount = charCount
    };
}

private static Dictionary<string, double> FindkNN(int k, Dictionary<string, List<kNNEntity>> kNNGroups, Bitmap imageTest, string charValue)
{
    var trainList = kNNGroups[charValue];
    var imageArray = BitmapToIntArray(imageTest);
    Dictionary<int, double> kDict = new Dictionary<int, double>();
    List<double> distanceList = new List<double>();
    //Stopwatch stw = new Stopwatch();
    //stw.Reset();

    foreach (var trainElement in trainList)
    {
        //stw.Start();
        var distance = kNNDistance(imageArray, trainElement.array);
        //stw.Stop();

        //Console.WriteLine("Time: {0}", stw.Elapsed);
        //stw.Reset();
        distanceList.Add(distance);
    }

    for (int n = 0; n < k; n++)
    {
        var min = distanceList.Min();
        var index = distanceList.IndexOf(min);
        //Console.WriteLine("min: " + min + " index: " + index);
        kDict.Add(index, min);
        distanceList[index] = double.MaxValue;
    }

    // kDict predstavlja rječnik sa indexom vrijednosti koja se koristi kasnije za pronalaženje imena fonta
    // i udaljenosti dobivene iz kNN algoritma
    var sum = kDict.OrderBy(x => x.Value).Take(k).Sum(x => x.Value);
    //kDict.Sum(x => x.Value);

    //Console.WriteLine(testFontValue);
    //Console.WriteLine(sum);
    kDict = kDict.ToDictionary(x => x.Key, x => Math.Round(Math.Abs(x.Value - sum), 3));
    sum = kDict.OrderBy(x => x.Value).Take(k).Sum(x => x.Value);
    //Console.WriteLine(sum);
    if (sum != 0)
        kDict = kDict.ToDictionary(x => x.Key, x => Math.Round(x.Value / sum, 3));

    Dictionary<string, double> resultDict = new Dictionary<string, double>();

    foreach (var element in kDict)
    {
        var currentFont = trainList[element.Key].font;
        var currentValue = element.Value;

        if (resultDict.ContainsKey(currentFont))
            resultDict[currentFont] += currentValue;
        else
            resultDict.Add(currentFont, currentValue); 
    }

    return resultDict;
}

private static double kNNDistance(int[] testArr, int[] refArr)
{
    var resultArr = testArr.Select((x, index) => 
        Math.Abs(x - refArr[index]))
        .ToArray();

    return resultArr.Sum();
}

private static int[] BitmapToIntArray(Bitmap img)
{
    int counter = 0;
    int[] array = new int[img.Width*img.Height];

    for (int x = 0; x < img.Width; x++)
    {
        for (int y = 0; y < img.Height; y++)
        {
            Color originalColor = img.GetPixel(x, y);
            int grayScale = (int)((originalColor.R * 0.3) + (originalColor.G * 0.59) + (originalColor.B * 0.11));
            array[counter] = grayScale;
            counter++;
        }
    }

    //Console.Write(String.Join(", ", array));
    //Console.WriteLine("Count: " + array.Count());
    //Console.ReadLine();

    return array;
}

public static Bitmap ScaleImage(Bitmap image, int maxWidth, int maxHeight)
{
    var ratioX = (double)maxWidth / image.Width;
    var ratioY = (double)maxHeight / image.Height;
    var ratio = Math.Min(ratioX, ratioY);

    var newWidth = (int)(image.Width * ratio);
    var newHeight = (int)(image.Height * ratio);

    var newImage = new Bitmap(maxWidth, maxHeight);

    using (var graphics = Graphics.FromImage(newImage))
    {
        graphics.Clear(Color.White);
        graphics.DrawImage(image, 0, 0, newWidth, newHeight);
    }

    return newImage;
}

public static Rectangle FindCharLocation(int left, int right, int top, int bottom)
{
    int xSize = right - left;
    int ySize = bottom - top;
    return new Rectangle(left, top, xSize, ySize);
}
