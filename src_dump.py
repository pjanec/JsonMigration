import os
import sys
import fnmatch

# Supported file extensions to include
FILE_EXTENSIONS = ['.cs', '.csproj', '.vue', '.js', '.json', '.iss', '.ini', '.ps1', '.psm1', '.psd1']

# Glob-style folder/file ignore patterns
# Added 'bin' and 'obj' to the directory masks to exclude build artifacts.
IGNORED_DIR_MASKS = ['.*', 'bin', 'obj', 'dist', 'node_modules', 'dist-server', 'Output', 'TwoMachines']
IGNORED_FILE_MASKS = [
    '.*', '*.tmp', '*.bak', '*.swp', '*.user', '*.suo'
]

def get_unique_output_filename(base_path):
    """
    Finds an available output filename. If base_path exists, it appends a
    counter, e.g., output_1.txt.
    """
    # If the base path doesn't have an extension, add .txt
    if not os.path.splitext(base_path)[1]:
        base_path += '.txt'
    
    # First, check if the original path is available to use.
    if not os.path.exists(base_path):
        return base_path
        
    base, ext = os.path.splitext(base_path)
    counter = 1
    # If not, loop until a non-existent filename with a counter is found.
    while True:
        candidate = f"{base}_{counter}{ext}"
        if not os.path.exists(candidate):
            return candidate
        counter += 1

def matches_any_mask(name, mask_list):
    """
    Checks if a given name (file or directory) matches any of the glob patterns.
    """
    return any(fnmatch.fnmatchcase(name, pattern) for pattern in mask_list)

def collect_cs_files(input_dirs, output_file_base):
    """
    Walks input directories and concatenates content of specified file types
    into a single output file, ignoring specified patterns.
    """
    output_file = get_unique_output_filename(output_file_base)
    print(f"Output will be written to {output_file}")

    found_files = False
    try:
        with open(output_file, 'w', encoding='utf-8') as outfile:
            for input_dir in input_dirs:
                if not os.path.isdir(input_dir):
                    print(f"Warning: '{input_dir}' is not a valid directory. Skipping.")
                    continue

                print(f"Processing directory: {input_dir}")
                for root, dirs, files in os.walk(input_dir, topdown=True):
                    
                    # Filter out directories based on the ignore mask
                    dirs[:] = [
                        d for d in dirs
                        if not matches_any_mask(d, IGNORED_DIR_MASKS)
                    ]

                    # Process files in the current directory
                    for filename in files:
                        if matches_any_mask(filename, IGNORED_FILE_MASKS):
                            continue

                        if any(filename.lower().endswith(ext) for ext in FILE_EXTENSIONS):
                            found_files = True
                            file_path = os.path.join(root, filename)
                            
                            rel_path = os.path.relpath(file_path, input_dir)
                            header_path = os.path.join(os.path.basename(input_dir), rel_path)
                            header_path = header_path.replace("\\", "/")

                            outfile.write('\n')
                            outfile.write(f'//{"=" * 80}\n')
                            outfile.write(f'// File: {header_path}\n')
                            outfile.write(f'//{"=" * 80}\n\n')

                            try:
                                with open(file_path, 'r', encoding='utf-8', errors='ignore') as infile:
                                    outfile.write(infile.read())
                                    outfile.write('\n')
                                
                                outfile.flush()

                            except Exception as e:
                                outfile.write(f"// Error reading file: {e}\n")
    except Exception as e:
        print(f"\nAn unexpected error occurred: {e}")
        # Attempt to clean up the potentially empty/incomplete file
        if not found_files and os.path.exists(output_file):
            os.remove(output_file)
        sys.exit(1)

    if not found_files:
        print("\nWarning: No matching files were found to process.")
        # Clean up the empty file that was created
        if os.path.exists(output_file):
            os.remove(output_file)
            print(f"Removed empty output file: {output_file}")
    else:
        print(f"\nDone. Output successfully written to {output_file}")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python cs_dump.py <input_folder_1> [input_folder_2...] <output_file_base>")
        sys.exit(1)

    input_folders = sys.argv[1:-1]
    output_file_base = sys.argv[-1]

    collect_cs_files(input_folders, output_file_base)
