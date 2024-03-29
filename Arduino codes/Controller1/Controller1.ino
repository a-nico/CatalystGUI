
// Stepper Stuff
#define numOfMotors 4
const int motorPin[numOfMotors][4] = 
  { {22, 23, 24, 25}, {28, 29, 30, 31}, {34, 35, 36, 37}, {40, 41, 42, 43} }; // row is motor, column is pin
int stepperState[numOfMotors] = {0}; // state machine for stepper motors
int steps[numOfMotors] = {0}; // steps for steppers to move: 0 means doesn't have to move
int requestedSteps[numOfMotors]; // requests to update "steps", updated by Serial
bool newRequest[numOfMotors] = {false}; // keeps track if request was fulfilled 
long lastStepTime[numOfMotors] = {0}; // to space out the steps
#define stepPause 5 // ms to wait between steps (basically sets stepper speed)

// Serial stuff
int parseState = 0;
char commandType = 0; // the letter to say which function to call
char buf[15];
int idx  = 0; // buffer index
int valuesArrayIndex = 0;
int valuesArray[5] = {0}; // numbers to be passed to functions (e.g. sensor #, fan percent, temperature, pressure)

#define solenoidPin 4
#define fanOnOffPin 5
#define LEDringPin 6

void setup() 
{
  pinMode(solenoidPin, OUTPUT);
  pinMode(LEDringPin, OUTPUT);
  pinMode(fanOnOffPin, OUTPUT);

  for (int motor = 0; motor < numOfMotors; motor++) 
  {
    pinMode(motorPin[motor][0], OUTPUT);
    pinMode(motorPin[motor][1], OUTPUT);
    pinMode(motorPin[motor][2], OUTPUT);
    pinMode(motorPin[motor][3], OUTPUT);
  }
  // set registers for timer3
  // pins 2, 3, 5 use timer3, see https://arduino-info.wikispaces.com/Timers-Arduino
  TCCR3A = B10100010;
  TCCR3B = B00010001; // 001 (no prescale)
  ICR3 = 320; // TOP = 320 for 25 kHZ
  OCR3B = 32; // pin 2 goes by OCR3B. Start fan low.
  pinMode(2, OUTPUT);
  // frequency is 16,000,000 / prescale / ICRx / 2
  // duty cycle will be OCRBxN / ICRx, look on datasheet or experiment
  
  Serial.begin(115200); // max baud rate
}

void loop()
{

UpdateCommands();
//delay(10);
//stepper(1);
//delay(10);
//stepper(0);
for (int m = 0; m < numOfMotors; m++)
{
  stepper(m);
}


}

// reads Serial
void UpdateCommands()
{
  int currentByte = Serial.read();
  if (-1 == currentByte)
  {
    return;
  }
  char currentChar = (char) currentByte;
  switch (parseState)
  {
    case 0:      // idle
      if ('%' == currentChar)
      {
        parseState = 1;
        idx = 0;
        valuesArrayIndex = 0;
      }
    break;
    
    case 1:    // saw a "%" char, so looks for command
      // list here all allowable commantType chars:
      if ('A' == currentChar || 'M' == currentChar || 'F' == currentChar || 'W' == currentChar || 'R' == currentChar)
      {
        commandType = currentChar;
        parseState = 2;
      }
      else
      { // means got some garbage after the "%", reset
        parseState = 0;
      }
      break;
        
    case 2:    // param
      if (';' == currentChar)
      {
        buf[idx] = '\0'; // makes a null terminated string so atoi() stops there
        valuesArray[valuesArrayIndex] = atoi(buf);
        idx = 0; // got the first integer
        parseState = 0;
        doTasks(commandType, valuesArray);
      }
      else if (',' == currentChar)
      {
        buf[idx] = '\0';
        valuesArray[valuesArrayIndex] = atoi(buf); // capture the number, store in valuesArray
        valuesArrayIndex++; // moves over since it wrote a number 
        idx = 0; // resets index buffer to ready it for the next number
      }
      else
      {
        buf[idx] = currentChar; // builds up the number until "," or ";"
        idx++;
      }
      //break; // no need because it's last
  } // end switch
}

// perform the tasks requested by serial
void doTasks(char command, int values[])
{
 switch (command)
 {
  case 'A': // reads analog pin (e.g.  %A0;)
    {// has only one value (value[0]) which is the pin to read
    Serial.print( "A," + (String)values[0] + "," + analogRead(values[0]) + ";");
    }
    break;
    
  case 'F': // sets fan speed percent (e.g.  %F90; sets fan to 90%)
    if (values[0] > 100) values[0] = 100;
    OCR3B = values[0] * ICR3 / 100; // pin 2 on MEGA
    break;

  case 'M': // %M(a),(b); requests motor (a) to move # of steps (b) (e.g.  %M2,-100; makes motor 2 go 100 steps clockwise)
    requestedSteps[values[0]] = values[1];
    newRequest[values[0]] = true;
    break;

  case 'W': 
    // gives direct control to digital pins output (write). Careful to be a writeable pin (pinMode)
    // %W,8,1; means pin8 HIGH, %W,8,0; is pin8 LOW
    if (0 == values[1]) 
    {
      digitalWrite(values[0], LOW);
    } 
    else if (1 == values[1]) 
    {
      digitalWrite(values[0], HIGH);
    }
    break;

  case 'R': // reads digital pin. %R7; reads state of digital pin7, returns 1 or 0 for HIGH/LOW
    Serial.print( "D," + (String)values[0] + "," + (String)digitalRead(values[0]) + ";");
    
  default: break;
 }

}

// STEPPER MOTOR
// moves "motor" by requestedSteps 
void stepper(int motor)    // requestedSteps is updated by Serial.
{
  if (millis() - lastStepTime[motor] < stepPause)
  { //Serial.println( (String)motor + " too early");
    return; // too early to step again
  }
  
  if (newRequest[motor] && 0 == stepperState[motor])
  {
      // so won't change direction before it finishes all 8 sequences
      steps[motor] = requestedSteps[motor];
      newRequest[motor] = false;
      stepperState[motor] = 1;
  }
  
  if (0 == steps[motor] && 0 == stepperState[motor]) 
  {
    return;
  }
  else if (steps[motor] < 0) // clockwise
  { 
    switch (stepperState[motor])
    {
     case 0:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 1:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 2:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 3:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 4 :
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 5:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     case 6:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     case 7:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     default:
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       steps[motor]++; // signal that one CW step was completed
       stepperState[motor] = -1; // that way it's 0 after the ++
       break;
    }
  }
  else
  { // counterclockwise
    switch (stepperState[motor])
    {
     case 0 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 1 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 2 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 3 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 4 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 5 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     case 6 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     case 7 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     default :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       stepperState[motor] = -1; // that way it's 0 after the ++
       steps[motor]--; // signal that one CCW step was completed
       break;
    }
  }
  stepperState[motor]++;
  lastStepTime[motor] = millis();
  //Serial.println( (String)motor + " moved");
}
