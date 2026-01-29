#!/usr/bin/env python3
"""
Convert PDF to Markdown format
"""

import sys
import re
from pathlib import Path

def extract_text_simple(pdf_path):
    """Simple PDF text extraction using basic parsing"""
    try:
        with open(pdf_path, 'rb') as f:
            content = f.read()
        
        # Validate PDF header
        if len(content) < 8 or not content[:4] == b'%PDF':
            raise ValueError("Invalid PDF file")
        
        # Convert to string for text extraction
        text_content = content.decode('latin-1', errors='ignore')
        
        # Extract text from BT/ET blocks
        text_parts = []
        index = 0
        while index < len(text_content):
            bt_index = text_content.find('BT', index)
            if bt_index < 0:
                break
            
            et_index = text_content.find('ET', bt_index)
            if et_index < 0:
                break
            
            block = text_content[bt_index + 2:et_index]
            
            # Extract text from parentheses (PDF text strings)
            for match in re.finditer(r'\(([^)]+)\)', block):
                text = match.group(1)
                # Decode PDF escape sequences
                text = text.replace('\\n', '\n')
                text = text.replace('\\r', '\r')
                text = text.replace('\\t', '\t')
                text = text.replace('\\(', '(')
                text = text.replace('\\)', ')')
                text = text.replace('\\\\', '\\')
                text_parts.append(text)
            
            index = et_index + 2
        
        return '\n'.join(text_parts)
    except Exception as e:
        raise Exception(f"Failed to extract text: {e}")

def text_to_markdown(text):
    """Convert plain text to markdown format"""
    lines = text.split('\n')
    markdown_lines = []
    
    for line in lines:
        line = line.strip()
        if not line:
            markdown_lines.append('')
            continue
        
        # Detect headings (lines that are short and all caps or have specific patterns)
        if len(line) < 80 and line.isupper() and len(line.split()) < 10:
            markdown_lines.append(f'## {line}')
        # Detect numbered lists
        elif re.match(r'^\d+[\.\)]\s+', line):
            markdown_lines.append(line)
        # Detect bullet points
        elif re.match(r'^[-*•]\s+', line):
            markdown_lines.append(line)
        else:
            markdown_lines.append(line)
    
    return '\n'.join(markdown_lines)

def main():
    if len(sys.argv) < 2:
        print("Usage: python3 pdf_to_markdown.py <pdf_file> [output_file]")
        sys.exit(1)
    
    pdf_path = Path(sys.argv[1])
    if not pdf_path.exists():
        print(f"Error: File not found: {pdf_path}")
        sys.exit(1)
    
    output_path = sys.argv[2] if len(sys.argv) > 2 else pdf_path.with_suffix('.md')
    
    print(f"Extracting text from {pdf_path}...")
    try:
        text = extract_text_simple(pdf_path)
        
        if not text or not text.strip():
            print("Warning: No text extracted. The PDF might be image-based or encrypted.")
            print("Trying alternative method...")
            # Try using macOS textutil if available
            import subprocess
            try:
                result = subprocess.run(
                    ['textutil', '-convert', 'txt', '-stdout', str(pdf_path)],
                    capture_output=True,
                    text=True,
                    timeout=30
                )
                if result.returncode == 0 and result.stdout.strip():
                    text = result.stdout
                else:
                    raise Exception("textutil failed")
            except:
                print("Error: Could not extract text from PDF")
                sys.exit(1)
        
        print(f"Converting to markdown format...")
        markdown = text_to_markdown(text)
        
        print(f"Writing to {output_path}...")
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(markdown)
        
        print(f"✅ Successfully converted PDF to markdown!")
        print(f"   Output: {output_path}")
        print(f"   Size: {len(markdown)} characters")
        
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)

if __name__ == '__main__':
    main()
