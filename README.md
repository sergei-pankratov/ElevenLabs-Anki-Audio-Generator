# ElevenLabs Anki Audio Generator

A C# program that adds text-to-speech audio to Anki flashcard decks using the ElevenLabs API. Works with any language supported by ElevenLabs.

## What it does

This program analyzes your Anki deck, identifies text content, and:
1. Adds audio references (`[sound:filename.mp3]`) to your cards
2. Generates a text file with sentences and corresponding audio filenames
3. Creates MP3 audio files using ElevenLabs text-to-speech API
4. Outputs audio files directly to your Anki media folder

## Prerequisites

- .NET 6.0 or higher
- ElevenLabs API key
- Anki deck exported in **old format** (anki21, not anki21b)
- Basic understanding of your deck's field structure

## Setup

1. **Get ElevenLabs API Key**
   - Sign up at [ElevenLabs](https://elevenlabs.io/)
   - Get your API key from the profile section

2. **Create .env file** (optional)
   ```
   ELEVEN_LABS_API_KEY=your_api_key_here
   ```

3. **Install dependencies**
   ```bash
   dotnet restore
   ```

## Step-by-Step Workflow

### Step 1: Export Your Anki Deck

1. In Anki, go to **File → Export**
2. **IMPORTANT**: Choose **"Anki Deck Package (*.apkg)"** 
3. **IMPORTANT**: Uncheck **"Support older Anki versions"** to get anki21 format (not anki21b)
4. Export your deck

### Step 2: Extract the Deck Files

1. **Rename** your `.apkg` file to `.zip`
2. **Extract** the zip file to a folder
3. You should see files like:
   ```
   your_deck/
   ├── collection.anki21
   └── media/
   ```

### Step 3: Customize and Run the Program

1. **Place the extracted folder** in your project directory

2. **Update the database path** in `Program.cs`:
   ```csharp
   var dbPath = "your_folder_name/collection.anki21";
   ```

3. **IMPORTANT: Identify your target field**
   - First run **Option 1** to analyze your deck structure
   - Note which field index contains the text you want to convert to audio
   - Update the field index in the code (currently set to field `[4]` for Czech frequency dictionary)

4. **Customize for your deck** (see [Customization](#customization) section below for details):
   - Target field index (which field contains your text)
   - Language code and voice selection
   - Audio file naming prefix

6. **Run the program**:
   ```bash
   dotnet run
   ```

### Step 4: Choose Your Workflow

The program offers 5 options:

#### Option 1: Analyze Database Structure
- Examines your deck structure
- Shows note types, fields, and sample data
- Use this first to understand your deck

#### Option 2: Modify Notes (Dry Run)
- Shows what changes will be made
- No actual modifications
- Good for testing

#### Option 3: Generate Text File and Modify Notes ⭐ **RECOMMENDED FIRST**
- Updates database with audio references
- Creates `czech_audio_list.txt` with sentences and filenames
- No audio generation yet

#### Option 4: Generate Audio from Text File ⭐ **RECOMMENDED SECOND**
- Reads from `czech_audio_list.txt`
- Generates MP3 files using ElevenLabs API
- Outputs directly to Anki media folder

#### Option 5: All-in-One
- Does everything in one step
- Good if you're confident about the process

### Step 5: Pack and Import Back to Anki

1. **Zip the modified folder** back to `.zip`
2. **Rename** from `.zip` to `.apkg`
3. **Import** into Anki via **File → Import**
4. **Run Tools → Check Media** in Anki to refresh the media database

## Text File Format

The generated `czech_audio_list.txt` uses pipe-separated format:
```
_czech_frequency_1.mp3|První česká věta
_czech_frequency_2.mp3|Druhá česká věta s více slovy
_czech_frequency_3.mp3|Třetí věta pro testování
```

## Audio File Output

Audio files are generated directly in:
```
C:\Users\[username]\AppData\Roaming\Anki2\User 1\collection.media\
```

Files are named with customizable prefix: `_czech_frequency_1.mp3`, `_czech_frequency_2.mp3`, etc.

**To customize the file prefix**: Update this line in `ModifyNotesWithAudio`:
```csharp
audioFileName = $"_your_custom_prefix_{audioFileCounter}.mp3";
```

## Customization

This program was originally designed for Czech frequency dictionaries but can be adapted for any language and deck type.

### Key Areas to Customize:

#### 1. Target Field Index
Update the field index that contains your text (currently `[4]` for Czech frequency dictionary):
```csharp
// In ModifyNotesWithAudio method, change this line:
if (fieldValues.Count > 4)  // Change 4 to your field index
{
    var textContent = fieldValues[4].Trim();  // Change 4 to your field index
```

#### 2. Language and Voice Settings
Update language code and voice selection (in `GenerateCzechSpeech` method):
```csharp
// Change language code:
language_code = "cs",  // "en" for English, "es" for Spanish, "fr" for French, etc.

// Change voice selection (find your preferred voice name):
if (name.Equals("GEORGE", StringComparison.OrdinalIgnoreCase))  // Change "GEORGE" to your preferred voice
```

#### 3. File Naming Prefix
Customize the audio file prefix:
```csharp
// In ModifyNotesWithAudio method:
audioFileName = $"_czech_frequency_{audioFileCounter}.mp3";  // Change "_czech_frequency_" to your prefix
```

#### 4. Text File Name
Update the text file name:
```csharp
// In ModifyNotesWithAudio method:
var textFilePath = "czech_audio_list.txt";  // Change to your preferred name
```

### Example Customizations:

**For English vocabulary deck:**
- Field index: `[2]` (if English word is in field 2)
- Language: `"en"`
- Prefix: `"_english_vocab_"`
- Voice: `"Rachel"` or `"Josh"`

**For Spanish sentences deck:**
- Field index: `[1]` (if Spanish sentence is in field 1)  
- Language: `"es"`
- Prefix: `"_spanish_sentences_"`
- Voice: `"Matias"` or `"Isabella"`

## Recommended Workflow

1. **Export deck** in old format (anki21)
2. **Extract** .apkg → .zip → folder
3. **Run Option 1** - Analyze your deck structure first
4. **Customize** the code for your specific deck (see [Customization](#customization))
5. **Run Option 3** - Generate text file and modify database
6. **Review** `czech_audio_list.txt` to verify sentences
7. **Run Option 4** - Generate audio from text file
8. **Pack back** folder → .zip → .apkg
9. **Import** to Anki and run **Tools → Check Media**

## Features

- ✅ Supports Czech language with proper language codes
- ✅ Uses GEORGE voice (or fallback to first available voice)
- ✅ Rate limiting to avoid API limits (1-second delays)
- ✅ Progress tracking for audio generation
- ✅ Direct output to Anki media folder
- ✅ Separates text file generation from audio generation
- ✅ Error handling and detailed logging

## Troubleshooting

### "Database not found"
- Check the path in `Program.cs` matches your extracted folder
- Ensure you extracted the .apkg file correctly

### "Missing files" or "Unused files" in Anki
- Run **Tools → Check Media** in Anki after importing
- This refreshes Anki's media database

### Audio not playing
- Ensure audio files are in the correct media folder
- Check that audio references match actual filenames
- Verify the audio files were generated successfully

### API Rate Limits
- The program includes 1-second delays between API calls
- For large decks, generation may take a while
- You can resume using Option 4 if interrupted

## Project Structure

```
ElevenCzech/
├── Program.cs              # Main program
├── ElevenCzech.csproj     # Project file
├── README.md              # This file
├── .env                   # API key (optional)
├── czech_audio_list.txt   # Generated text file
└── your_deck_folder/      # Extracted Anki deck
    ├── collection.anki21  # Database file
    └── media/             # Media files
```

## Dependencies

- Microsoft.Data.Sqlite
- DotNetEnv
- System.Text.Json (built-in)

## License

MIT License - Feel free to modify and distribute. 