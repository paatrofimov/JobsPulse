using System.Collections.Frozen;
using System.Text;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Reads a <see cref="LocationRegion"/> out of the free text an ATS calls a location. There is no country field
/// anywhere in the pipeline - «Berlin, Germany», «EMEA - Remote» and «Москва» are all a source ever says - so the
/// answer comes from a keyword table: countries, capitals and the tech hubs the boards actually name.
///
/// Two rules make the table behave:
///
/// - geography wins over «remote»: «Remote, Poland» is Europe, not <see cref="LocationRegion.Remote"/>, because the
///   reader who groups by location wants the country. Only a posting with no place at all is remote.
/// - the regions are tested in enum order, so a vacancy open in Berlin and New York counts as European - the
///   priority the whole screen is built around.
///
/// Matching is by whole word over a normalized string, which is what keeps «Cork» out of «Corktown» and lets a
/// multi-word key like «tel aviv» be one key. An unrecognized location is <see cref="LocationRegion.Unknown"/> and
/// still shown - a miss must never hide a vacancy.
/// </summary>
public static class LocationRegions
{
    /// <summary>Enum order is the display order - see <see cref="LocationRegion"/>.</summary>
    public static readonly IReadOnlyList<LocationRegion> Ordered = [.. Enum.GetValues<LocationRegion>()];

    public static LocationRegion Of(Vacancy vacancy) => Of(vacancy.Location, vacancy.Offices);

    public static LocationRegion Of(string? location, IReadOnlyList<string> offices)
    {
        var text = Normalize(location, offices);

        if (text.Length == 0)
            return LocationRegion.Unknown;

        foreach (var region in Ordered)
        {
            if (region is LocationRegion.Remote or LocationRegion.Unknown)
                continue;

            if (Matches(text, Keys[region]))
                return region;
        }

        return Matches(text, RemoteKeys) ? LocationRegion.Remote : LocationRegion.Unknown;
    }

    /// <summary>
    /// The region of every board a feed mentions, keyed '{sourceId}/{boardId}' - the key shape the vacancy counts
    /// use. A company hires in several places, so the region is the one most of its vacancies name; a tie goes to the
    /// earlier region, which means Europe wins it. A board with no vacancies in the feed is simply absent.
    /// </summary>
    public static IReadOnlyDictionary<string, LocationRegion> ByBoard(IReadOnlyList<Vacancy> vacancies) =>
        vacancies
            .GroupBy(v => $"{v.SourceId}/{v.BoardId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(Of)
                    .OrderByDescending(r => r.Count())
                    .ThenBy(r => r.Key)
                    .First()
                    .Key,
                StringComparer.OrdinalIgnoreCase);

    public static string Name(LocationRegion region, BotLanguage language) =>
        BotTexts.Get(
            region switch
            {
                LocationRegion.Europe => TextKey.RegionEurope,
                LocationRegion.Remote => TextKey.RegionRemote,
                LocationRegion.Cis => TextKey.RegionCis,
                LocationRegion.Americas => TextKey.RegionAmericas,
                LocationRegion.Asia => TextKey.RegionAsia,
                LocationRegion.MiddleEastAndAfrica => TextKey.RegionMiddleEastAndAfrica,
                LocationRegion.Oceania => TextKey.RegionOceania,
                _ => TextKey.RegionUnknown
            },
            language);

    public static string Glyph(LocationRegion region) =>
        region switch
        {
            LocationRegion.Europe => "🇪🇺",
            LocationRegion.Remote => "🌐",
            LocationRegion.Cis => "🧭",
            LocationRegion.Americas => "🌎",
            LocationRegion.Asia => "🌏",
            LocationRegion.MiddleEastAndAfrica => "🌍",
            LocationRegion.Oceania => "🦘",
            _ => "❔"
        };

    /// <summary>
    /// Everything the location may be spelled as, reduced to lowercase words separated by single spaces and padded,
    /// so a key can be tested as <c>" key "</c> - whole words, one test for single and multi-word keys alike.
    /// </summary>
    private static string Normalize(string? location, IReadOnlyList<string> offices)
    {
        var sb = new StringBuilder(" ");

        Append(sb, location);

        foreach (var office in offices)
            Append(sb, office);

        return sb.Length == 1 ? string.Empty : sb.ToString();
    }

    private static void Append(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var spaced = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                spaced = false;

                continue;
            }

            if (!spaced)
            {
                sb.Append(' ');
                spaced = true;
            }
        }

        if (!spaced)
            sb.Append(' ');
    }

    private static bool Matches(string text, FrozenSet<string> keys)
    {
        foreach (var key in keys)
        {
            if (text.Contains(key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Keys are stored already padded - the padding is what makes the match a whole-word one.</summary>
    private static FrozenSet<string> Set(params string[] keys) =>
        keys.Select(k => $" {k} ").ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> RemoteKeys = Set(
        "remote", "remotely", "anywhere", "worldwide", "work from home", "home office", "wfh", "telecommute",
        "distributed", "virtual", "удаленно", "удалённо", "дистанционно", "удаленная работа");

    private static readonly FrozenDictionary<LocationRegion, FrozenSet<string>> Keys =
        new Dictionary<LocationRegion, FrozenSet<string>>
        {
            [LocationRegion.Europe] = Set(
                "europe", "european union", "eu", "emea", "benelux", "dach", "nordics", "nordic", "baltics", "europa",
                "европа",
                "austria", "vienna", "graz", "linz", "österreich",
                "belgium", "brussels", "antwerp", "ghent", "leuven",
                "bulgaria", "sofia", "plovdiv", "varna",
                "croatia", "zagreb", "split",
                "cyprus", "nicosia", "limassol", "larnaca",
                "czech", "czechia", "czech republic", "prague", "praha", "brno", "ostrava",
                "denmark", "copenhagen", "aarhus", "odense",
                "estonia", "tallinn", "tartu",
                "finland", "helsinki", "espoo", "tampere", "oulu",
                "france", "paris", "lyon", "toulouse", "bordeaux", "nantes", "lille", "marseille", "nice",
                "grenoble", "sophia antipolis", "rennes", "strasbourg",
                "germany", "deutschland", "berlin", "munich", "muenchen", "münchen", "hamburg", "frankfurt",
                "cologne", "koeln", "köln", "stuttgart", "duesseldorf", "düsseldorf", "dusseldorf", "leipzig",
                "dresden", "karlsruhe", "nuremberg", "nürnberg", "hannover", "bremen", "bonn", "mannheim", "aachen",
                "greece", "athens", "thessaloniki",
                "hungary", "budapest", "debrecen", "szeged",
                "iceland", "reykjavik",
                "ireland", "dublin", "cork", "galway", "limerick",
                "italy", "italia", "milan", "milano", "rome", "roma", "turin", "torino", "bologna", "naples",
                "florence", "firenze", "pisa", "padova", "genoa",
                "latvia", "riga",
                "lithuania", "vilnius", "kaunas",
                "luxembourg",
                "malta", "valletta",
                "moldova", "chisinau",
                "monaco", "andorra", "liechtenstein", "san marino",
                "montenegro", "podgorica", "albania", "tirana", "bosnia", "sarajevo", "banja luka",
                "serbia", "belgrade", "beograd", "novi sad", "nis",
                "north macedonia", "macedonia", "skopje",
                "netherlands", "holland", "nederland", "amsterdam", "rotterdam", "utrecht", "eindhoven", "the hague",
                "hague", "den haag", "delft", "groningen", "haarlem", "leiden", "nijmegen", "amstelveen",
                "norway", "oslo", "bergen", "trondheim", "stavanger",
                "poland", "polska", "warsaw", "warszawa", "krakow", "kraków", "cracow", "wroclaw", "wrocław",
                "poznan", "poznań", "gdansk", "gdańsk", "gdynia", "katowice", "lodz", "łódź", "szczecin", "lublin",
                "portugal", "lisbon", "lisboa", "porto", "braga", "coimbra", "aveiro",
                "romania", "bucharest", "bucuresti", "bucurești", "cluj", "cluj napoca", "timisoara", "timișoara",
                "iasi", "iași", "brasov", "sibiu",
                "slovakia", "bratislava", "kosice", "košice",
                "slovenia", "ljubljana", "maribor",
                "spain", "espana", "españa", "madrid", "barcelona", "valencia", "malaga", "málaga", "seville",
                "sevilla", "bilbao", "zaragoza", "alicante", "palma", "santander",
                "sweden", "sverige", "stockholm", "gothenburg", "goteborg", "göteborg", "malmo", "malmö", "lund",
                "uppsala", "linkoping", "linköping",
                "switzerland", "schweiz", "suisse", "zurich", "zuerich", "zürich", "geneva", "geneve", "genève",
                "basel", "lausanne", "bern", "zug", "lugano", "winterthur",
                "ukraine", "kyiv", "kiev", "lviv", "kharkiv", "odesa", "odessa", "dnipro", "vinnytsia", "україна",
                "київ", "львів",
                "united kingdom", "uk", "great britain", "britain", "england", "scotland", "wales",
                "northern ireland", "london", "manchester", "edinburgh", "glasgow", "bristol", "cambridge", "oxford",
                "birmingham", "leeds", "liverpool", "sheffield", "newcastle", "nottingham", "belfast", "cardiff",
                "reading", "brighton", "milton keynes", "aberdeen", "leicester"),

            [LocationRegion.Cis] = Set(
                "cis", "снг",
                "russia", "russian federation", "россия", "рф", "moscow", "москва", "москве", "saint petersburg",
                "st petersburg", "spb", "санкт петербург", "петербург", "novosibirsk", "новосибирск",
                "yekaterinburg", "ekaterinburg", "екатеринбург", "kazan", "казань", "nizhny novgorod",
                "нижний новгород", "samara", "самара", "perm", "пермь", "rostov", "ростов", "krasnodar",
                "краснодар", "voronezh", "воронеж", "ufa", "уфа", "chelyabinsk", "челябинск", "omsk", "омск",
                "tomsk", "томск", "innopolis", "иннополис",
                "belarus", "беларусь", "белоруссия", "minsk", "минск", "gomel", "гомель",
                "kazakhstan", "казахстан", "almaty", "алматы", "astana", "астана", "nur sultan",
                "uzbekistan", "узбекистан", "tashkent", "ташкент",
                "kyrgyzstan", "киргизия", "кыргызстан", "bishkek", "бишкек",
                "tajikistan", "таджикистан", "dushanbe",
                "armenia", "армения", "yerevan", "ереван",
                "georgia country", "tbilisi", "тбилиси", "грузия", "batumi", "батуми",
                "azerbaijan", "азербайджан", "baku", "баку"),

            [LocationRegion.Americas] = Set(
                "americas", "north america", "south america", "latam", "latin america",
                "usa", "u s a", "united states", "us", "america",
                "new york", "nyc", "brooklyn", "san francisco", "bay area", "silicon valley", "seattle", "bellevue",
                "austin", "boston", "cambridge ma", "chicago", "los angeles", "san diego", "san jose", "sunnyvale",
                "mountain view", "palo alto", "santa clara", "denver", "boulder", "atlanta", "dallas", "houston",
                "miami", "orlando", "tampa", "portland", "phoenix", "philadelphia", "pittsburgh", "detroit",
                "minneapolis", "salt lake city", "las vegas", "nashville", "charlotte", "raleigh", "durham",
                "washington dc", "arlington", "reston", "mclean", "columbus", "cleveland", "kansas city",
                "california", "texas", "florida", "virginia", "massachusetts", "washington state", "new jersey",
                "illinois", "colorado", "oregon", "utah", "arizona", "north carolina", "pennsylvania", "michigan",
                "canada", "toronto", "vancouver", "montreal", "ottawa", "calgary", "edmonton", "waterloo",
                "mississauga", "quebec", "ontario", "british columbia", "halifax", "winnipeg",
                "mexico", "mexico city", "guadalajara", "monterrey", "queretaro",
                "brazil", "brasil", "sao paulo", "são paulo", "rio de janeiro", "belo horizonte", "curitiba",
                "porto alegre", "recife", "florianopolis",
                "argentina", "buenos aires", "cordoba", "rosario",
                "chile", "santiago", "colombia", "bogota", "bogotá", "medellin", "medellín", "peru", "lima",
                "uruguay", "montevideo", "paraguay", "asuncion", "ecuador", "quito", "guayaquil",
                "costa rica", "san jose costa rica", "panama", "guatemala", "dominican republic", "santo domingo"),

            [LocationRegion.Asia] = Set(
                "asia", "apac", "southeast asia", "south asia",
                "india", "bangalore", "bengaluru", "hyderabad", "pune", "chennai", "mumbai", "delhi", "new delhi",
                "gurgaon", "gurugram", "noida", "kolkata", "ahmedabad", "kochi", "jaipur", "indore", "coimbatore",
                "china", "beijing", "shanghai", "shenzhen", "guangzhou", "hangzhou", "chengdu", "suzhou", "xian",
                "wuhan", "nanjing", "hong kong", "macau",
                "taiwan", "taipei", "hsinchu",
                "japan", "tokyo", "osaka", "kyoto", "yokohama", "fukuoka", "nagoya",
                "korea", "south korea", "seoul", "busan", "pangyo",
                "singapore",
                "malaysia", "kuala lumpur", "penang", "johor",
                "indonesia", "jakarta", "bandung", "surabaya", "bali",
                "thailand", "bangkok", "chiang mai", "phuket",
                "vietnam", "viet nam", "hanoi", "ho chi minh", "da nang", "saigon",
                "philippines", "manila", "makati", "taguig", "cebu", "davao",
                "pakistan", "karachi", "lahore", "islamabad",
                "bangladesh", "dhaka", "sri lanka", "colombo", "nepal", "kathmandu", "mongolia", "ulaanbaatar",
                "myanmar", "yangon", "cambodia", "phnom penh", "laos", "vientiane", "brunei"),

            [LocationRegion.MiddleEastAndAfrica] = Set(
                "middle east", "mena", "africa", "gulf",
                "israel", "tel aviv", "jerusalem", "haifa", "herzliya", "ramat gan", "petah tikva", "beer sheva",
                "uae", "united arab emirates", "dubai", "abu dhabi", "sharjah",
                "saudi arabia", "saudi", "riyadh", "jeddah", "dammam", "neom", "khobar",
                "qatar", "doha", "kuwait", "bahrain", "manama", "oman", "muscat",
                "jordan", "amman", "lebanon", "beirut", "iraq", "baghdad", "erbil", "iran", "tehran",
                "turkey", "turkiye", "türkiye", "istanbul", "ankara", "izmir", "antalya", "bursa",
                "egypt", "cairo", "alexandria", "giza",
                "morocco", "casablanca", "rabat", "marrakech", "tangier",
                "tunisia", "tunis", "sfax", "algeria", "algiers", "libya", "tripoli",
                "nigeria", "lagos", "abuja", "ghana", "accra", "senegal", "dakar", "ivory coast", "abidjan",
                "kenya", "nairobi", "mombasa", "ethiopia", "addis ababa", "uganda", "kampala", "tanzania",
                "dar es salaam", "rwanda", "kigali", "zambia", "lusaka", "zimbabwe", "harare", "mozambique",
                "south africa", "johannesburg", "cape town", "pretoria", "durban", "sandton", "stellenbosch",
                "mauritius", "namibia", "windhoek", "botswana", "gaborone"),

            [LocationRegion.Oceania] = Set(
                "oceania", "australia", "sydney", "melbourne", "brisbane", "perth", "adelaide", "canberra",
                "gold coast", "hobart", "new south wales", "victoria australia", "queensland",
                "new zealand", "auckland", "wellington", "christchurch", "dunedin",
                "fiji", "suva", "papua new guinea", "port moresby")
        }.ToFrozenDictionary();
}
