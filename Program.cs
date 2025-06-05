using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace ElevenCzech
{
    internal class Program
    {
        static async Task<int> GenerateCzechSpeech(string apiKey, string czechText, string outputFilePath)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);

                // Get available voices
                var voicesResponse = await httpClient.GetAsync("https://api.elevenlabs.io/v1/voices");
                voicesResponse.EnsureSuccessStatusCode();
                var voicesJson = await voicesResponse.Content.ReadAsStringAsync();
                var voicesData = JsonSerializer.Deserialize<JsonElement>(voicesJson);

                string? selectedVoiceId = null;
                string? selectedVoiceName = null;

                if (voicesData.TryGetProperty("voices", out JsonElement voicesArray))
                {
                    var voicesList = voicesArray.EnumerateArray().ToList();
                    foreach (var voice in voicesList)
                    {
                        if (voice.TryGetProperty("name", out JsonElement nameElement) &&
                            voice.TryGetProperty("voice_id", out JsonElement idElement))
                        {
                            string name = nameElement.GetString() ?? "";
                            string id = idElement.GetString() ?? "";
                            if (name.Equals("GEORGE", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedVoiceId = id;
                                selectedVoiceName = name;
                                break;
                            }
                        }
                    }
                    // Fallback to first voice if GEORGE not found
                    if (selectedVoiceId == null && voicesList.Count > 0)
                    {
                        var firstVoice = voicesList[0];
                        if (firstVoice.TryGetProperty("voice_id", out JsonElement firstIdElement) &&
                            firstVoice.TryGetProperty("name", out JsonElement firstNameElement))
                        {
                            selectedVoiceId = firstIdElement.GetString();
                            selectedVoiceName = firstNameElement.GetString();
                        }
                    }
                }

                if (selectedVoiceId == null)
                {
                    return 1;
                }

                // Prepare the request payload with Czech language specification
                var requestPayload = new
                {
                    text = czechText,
                    model_id = "eleven_turbo_v2_5",
                    language_code = "cs",
                    voice_settings = new
                    {
                        stability = 0.5f,
                        similarity_boost = 0.8f,
                        style = 0.2f,
                        use_speaker_boost = true
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(
                    $"https://api.elevenlabs.io/v1/text-to-speech/{selectedVoiceId}?output_format=mp3_44100_128",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    var audioBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(outputFilePath, audioBytes);
                    return 0;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating speech: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return 3;
            }
        }

        static void AnalyzeAnkiDatabase(string dbPath)
        {
            Console.WriteLine("Analyzing Anki database...");
            
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // Get the models (note types) information
            var modelsCommand = connection.CreateCommand();
            modelsCommand.CommandText = "SELECT models FROM col";
            var modelsJson = modelsCommand.ExecuteScalar()?.ToString();
            
            if (!string.IsNullOrEmpty(modelsJson))
            {
                var models = JsonSerializer.Deserialize<JsonElement>(modelsJson);
                Console.WriteLine("\n=== NOTE TYPES (MODELS) ===");
                
                foreach (var modelProperty in models.EnumerateObject())
                {
                    var modelId = modelProperty.Name;
                    var model = modelProperty.Value;
                    
                    if (model.TryGetProperty("name", out var nameElement))
                    {
                        Console.WriteLine($"\nModel ID: {modelId}");
                        Console.WriteLine($"Name: {nameElement.GetString()}");
                        
                        if (model.TryGetProperty("flds", out var fieldsElement))
                        {
                            Console.WriteLine("Fields:");
                            var fieldIndex = 0;
                            foreach (var field in fieldsElement.EnumerateArray())
                            {
                                if (field.TryGetProperty("name", out var fieldNameElement))
                                {
                                    Console.WriteLine($"  [{fieldIndex}] {fieldNameElement.GetString()}");
                                    fieldIndex++;
                                }
                            }
                        }
                    }
                }
            }

            // Count total notes
            var notesCountCommand = connection.CreateCommand();
            notesCountCommand.CommandText = "SELECT COUNT(*) FROM notes";
            var notesCount = notesCountCommand.ExecuteScalar();
            Console.WriteLine($"\n=== STATISTICS ===");
            Console.WriteLine($"Total notes: {notesCount}");

            // Sample a few notes to understand the structure
            var sampleNotesCommand = connection.CreateCommand();
            sampleNotesCommand.CommandText = "SELECT id, mid, tags, flds FROM notes LIMIT 5";
            
            Console.WriteLine($"\n=== SAMPLE NOTES ===");
            using var reader = sampleNotesCommand.ExecuteReader();
            
            while (reader.Read())
            {
                var noteId = reader.GetInt64(0);
                var modelId = reader.GetInt64(1);
                var tags = reader.GetString(2);
                var fields = reader.GetString(3);
                
                Console.WriteLine($"\nNote ID: {noteId}");
                Console.WriteLine($"Model ID: {modelId}");
                Console.WriteLine($"Tags: '{tags}'");
                
                // Split fields by the unit separator (0x1f)
                var fieldValues = fields.Split('\x1f');
                Console.WriteLine("Field values:");
                for (int i = 0; i < fieldValues.Length; i++)
                {
                    var value = fieldValues[i];
                    if (value.Length > 100)
                        value = value.Substring(0, 100) + "...";
                    Console.WriteLine($"  [{i}] {value}");
                }
            }
        }

        static async Task ModifyNotesWithAudio(string dbPath, bool generateAudio = false, string? apiKey = null, bool generateTextFile = false, bool extractExistingAudio = false)
        {
            Console.WriteLine("\nAnalyzing notes for audio modification...");
            
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // Get all notes with their field values, ordered by note ID for consistent numbering
            var notesCommand = connection.CreateCommand();
            notesCommand.CommandText = "SELECT id, mid, flds FROM notes ORDER BY id";
            
            var notesToUpdate = new List<(long id, string originalFlds, string newFlds, string audioFile)>();
            var audioTextPairs = new List<(string audioFile, string czechText)>();
            
            using var reader = notesCommand.ExecuteReader();
            var processedCount = 0;
            var audioFileCounter = 1; // Start from 1 for audio file numbering
            
            while (reader.Read())
            {
                var noteId = reader.GetInt64(0);
                var modelId = reader.GetInt64(1);
                var fields = reader.GetString(2);
                
                // Split fields by the unit separator (0x1f)
                var fieldValues = fields.Split('\x1f').ToList();
                bool fieldsModified = false;
                string? audioFileName = null;
                
                // Only process field [4] (Czech sentence) for Czech Frequency Dictionary notes
                if (fieldValues.Count > 4)
                {
                    var czechSentence = fieldValues[4].Trim();
                    
                    if (!string.IsNullOrEmpty(czechSentence))
                    {
                        // Check if field already has audio
                        if (czechSentence.Contains("[sound:"))
                        {
                            // Extract existing audio reference and text for text file generation
                            if (extractExistingAudio || generateTextFile)
                            {
                                var soundMatch = System.Text.RegularExpressions.Regex.Match(czechSentence, @"\[sound:([^\]]+)\]");
                                if (soundMatch.Success)
                                {
                                    var existingAudioFile = soundMatch.Groups[1].Value;
                                    var textWithoutAudio = czechSentence.Replace(soundMatch.Value, "").Trim();
                                    audioTextPairs.Add((existingAudioFile, textWithoutAudio));
                                }
                            }
                        }
                        else
                        {
                            // Add new audio reference
                            audioFileName = $"_czech_frequency_{audioFileCounter}.mp3";
                            
                            // Add audio reference to the Czech sentence field
                            fieldValues[4] = $"{czechSentence} [sound:{audioFileName}]";
                            fieldsModified = true;
                            
                            // Store the audio file and text pair
                            audioTextPairs.Add((audioFileName, czechSentence));
                            audioFileCounter++;
                        }
                    }
                }
                
                if (fieldsModified)
                {
                    var newFields = string.Join("\x1f", fieldValues);
                    notesToUpdate.Add((noteId, fields, newFields, audioFileName!));
                }
                
                processedCount++;
                if (processedCount % 100 == 0)
                {
                    Console.WriteLine($"Processed {processedCount} notes...");
                }
            }
            
            reader.Close();
            
            Console.WriteLine($"\nFound {notesToUpdate.Count} notes to update with audio references");
            Console.WriteLine($"Found {audioTextPairs.Count} total audio entries (including existing)");
            
            if (notesToUpdate.Count > 0)
            {
                Console.WriteLine("Sample modifications:");
                for (int i = 0; i < Math.Min(5, notesToUpdate.Count); i++)
                {
                    var (id, original, modified, audioFile) = notesToUpdate[i];
                    Console.WriteLine($"\nNote {id}:");
                    
                    // Show just the Czech sentence field (field 4)
                    var originalFields = original.Split('\x1f');
                    var modifiedFields = modified.Split('\x1f');
                    
                    if (originalFields.Length > 4 && modifiedFields.Length > 4)
                    {
                        Console.WriteLine($"  Czech sentence (original): {originalFields[4]}");
                        Console.WriteLine($"  Czech sentence (modified): {modifiedFields[4]}");
                        Console.WriteLine($"  Audio file: {audioFile}");
                    }
                }
                
                Console.Write($"\nDo you want to apply these changes to {notesToUpdate.Count} notes? (y/N): ");
                var response = Console.ReadLine();
                
                if (response?.ToLowerInvariant() == "y")
                {
                    // Apply the updates
                    using var transaction = connection.BeginTransaction();
                    
                    var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = "UPDATE notes SET flds = @flds, mod = @mod WHERE id = @id";
                    
                    var fldsParam = updateCommand.CreateParameter();
                    fldsParam.ParameterName = "@flds";
                    updateCommand.Parameters.Add(fldsParam);
                    
                    var modParam = updateCommand.CreateParameter();
                    modParam.ParameterName = "@mod";
                    updateCommand.Parameters.Add(modParam);
                    
                    var idParam = updateCommand.CreateParameter();
                    idParam.ParameterName = "@id";
                    updateCommand.Parameters.Add(idParam);
                    
                    var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    
                    foreach (var (id, original, modified, audioFile) in notesToUpdate)
                    {
                        fldsParam.Value = modified;
                        modParam.Value = currentTimestamp;
                        idParam.Value = id;
                        
                        updateCommand.ExecuteNonQuery();
                    }
                    
                    transaction.Commit();
                    Console.WriteLine($"Successfully updated {notesToUpdate.Count} notes!");
                }
            }
            
            // Generate text file with sentences and filenames (regardless of whether notes were updated)
            if (generateTextFile && audioTextPairs.Count > 0)
            {
                var textFilePath = "czech_audio_list.txt";
                await File.WriteAllLinesAsync(textFilePath, 
                    audioTextPairs.Select(pair => $"{pair.audioFile}|{pair.czechText}"));
                Console.WriteLine($"Generated text file: {textFilePath} with {audioTextPairs.Count} entries");
                Console.WriteLine("Use option 4 to generate audio from this file.");
            }
            
            // Generate audio immediately if requested
            if (generateAudio && !string.IsNullOrEmpty(apiKey) && audioTextPairs.Count > 0)
            {
                Console.WriteLine("\nGenerating audio files...");
                await GenerateAudioFromList(audioTextPairs, apiKey);
            }
            
            if (notesToUpdate.Count > 0 && !generateTextFile && !generateAudio)
            {
                Console.WriteLine($"Audio files will be named: _czech_frequency_1.mp3 to _czech_frequency_{notesToUpdate.Count}.mp3");
            }
        }

        static async Task GenerateAudioFromList(List<(string audioFile, string czechText)> audioTextPairs, string apiKey)
        {
            var ankiMediaPath = @"C:\Users\tehne\AppData\Roaming\Anki2\User 1\collection.media";
            Directory.CreateDirectory(ankiMediaPath);
            
            Console.WriteLine($"Generating {audioTextPairs.Count} audio files in: {ankiMediaPath}");
            
            for (int i = 0; i < audioTextPairs.Count; i++)
            {
                var (audioFileName, czechText) = audioTextPairs[i];
                var audioPath = Path.Combine(ankiMediaPath, audioFileName);
                
                Console.WriteLine($"[{i + 1}/{audioTextPairs.Count}] Generating: {audioFileName}");
                Console.WriteLine($"Text: {czechText.Substring(0, Math.Min(60, czechText.Length))}...");
                
                var result = await GenerateCzechSpeech(apiKey, czechText, audioPath);
                
                if (result == 0)
                {
                    Console.WriteLine($"✓ Successfully generated: {audioFileName}");
                }
                else
                {
                    Console.WriteLine($"✗ Failed to generate: {audioFileName} (Error code: {result})");
                }
                
                // Add a small delay to avoid hitting API rate limits
                await Task.Delay(1000);
            }
            
            Console.WriteLine($"\nAudio generation complete!");
            Console.WriteLine($"Files generated in: {ankiMediaPath}");
            Console.WriteLine("Run Tools > Check Media in Anki to refresh the media database.");
        }

        static async Task GenerateAudioFromTextFile(string textFilePath, string apiKey)
        {
            if (!File.Exists(textFilePath))
            {
                Console.WriteLine($"Text file not found: {textFilePath}");
                return;
            }

            var lines = await File.ReadAllLinesAsync(textFilePath);
            var audioTextPairs = new List<(string audioFile, string czechText)>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split('|', 2);
                if (parts.Length == 2)
                {
                    audioTextPairs.Add((parts[0], parts[1]));
                }
            }

            Console.WriteLine($"Loaded {audioTextPairs.Count} entries from {textFilePath}");
            await GenerateAudioFromList(audioTextPairs, apiKey);
        }
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== ElevenCzech - Anki Audio Integration ===");

            dotenv.net.DotEnv.Load(new(ignoreExceptions: false));
            string? apiKey = Environment.GetEnvironmentVariable("ELEVEN_LABS_API_KEY");
            
            var dbPath = "_A_Frequency_Dictionary_of_Czech (4)/collection.anki21";
            
            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"Database not found at: {dbPath}");
                Console.WriteLine("Please ensure the Czech dictionary database is in the correct location.");
                return;
            }

            Console.WriteLine("\n1. Analyze database structure");
            Console.WriteLine("2. Modify notes with audio (dry run)");
            Console.WriteLine("3. Generate text file with sentences and modify notes");
            Console.WriteLine("4. Generate audio from text file");
            Console.WriteLine("5. Generate audio and modify notes (all in one)");
            Console.WriteLine("6. Extract existing audio to text file (regenerate text file)");
            Console.Write("\nSelect option (1-6): ");
            
            var choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    AnalyzeAnkiDatabase(dbPath);
                    break;
                    
                case "2":
                    await ModifyNotesWithAudio(dbPath, generateAudio: false);
                    break;
                    
                case "3":
                    await ModifyNotesWithAudio(dbPath, generateAudio: false, generateTextFile: true, extractExistingAudio: true);
                    break;
                    
                case "4":
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        Console.Write("Please enter your ElevenLabs API key: ");
                        apiKey = Console.ReadLine();
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            Console.WriteLine("API key is required for audio generation.");
                            return;
                        }
                    }
                    
                    Console.Write("Enter path to text file (default: czech_audio_list.txt): ");
                    var textFilePath = Console.ReadLine();
                    if (string.IsNullOrEmpty(textFilePath))
                        textFilePath = "czech_audio_list.txt";
                    
                    await GenerateAudioFromTextFile(textFilePath, apiKey);
                    break;
                    
                case "5":
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        Console.Write("Please enter your ElevenLabs API key: ");
                        apiKey = Console.ReadLine();
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            Console.WriteLine("API key is required for audio generation.");
                            return;
                        }
                    }
                    await ModifyNotesWithAudio(dbPath, generateAudio: true, apiKey);
                    break;
                    
                case "6":
                    await ModifyNotesWithAudio(dbPath, generateAudio: false, generateTextFile: true, extractExistingAudio: true);
                    break;
                    
                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
