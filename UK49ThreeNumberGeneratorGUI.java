import java.awt.BorderLayout;
import java.awt.Dimension;
import java.awt.Font;
import java.awt.GridLayout;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import javax.swing.JButton;
import javax.swing.JFrame;
import javax.swing.JPanel;
import javax.swing.SwingUtilities;

public class UK49ThreeNumberGeneratorGUI extends JFrame {
    private static final int TOTAL_LINES = 5;
    private static final int NUMBERS_PER_LINE = 3;
    private static final int MAX_NUMBER = 49;

    private final List<JButton> lineButtons;

    public UK49ThreeNumberGeneratorGUI() {
        super("UK49 Three Number Generator");

        lineButtons = new ArrayList<>();

        JPanel linesPanel = new JPanel(new GridLayout(TOTAL_LINES, 1, 8, 8));
        linesPanel.setPreferredSize(new Dimension(320, 220));

        for (int i = 0; i < TOTAL_LINES; i++) {
            JButton lineButton = new JButton();
            lineButton.setFont(new Font(Font.MONOSPACED, Font.BOLD, 18));
            lineButton.setFocusPainted(false);
            lineButtons.add(lineButton);
            linesPanel.add(lineButton);
        }

        JButton generateButton = new JButton("Generate 5 Lines");
        generateButton.addActionListener(event -> generateLines());

        JPanel buttonPanel = new JPanel();
        buttonPanel.add(generateButton);

        add(linesPanel, BorderLayout.CENTER);
        add(buttonPanel, BorderLayout.SOUTH);

        setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        pack();
        setLocationRelativeTo(null);

        generateLines();
    }

    private void generateLines() {
        for (int line = 1; line <= TOTAL_LINES; line++) {
            List<Integer> numbers = new ArrayList<>();

            for (int i = 1; i <= MAX_NUMBER; i++) {
                numbers.add(i);
            }

            Collections.shuffle(numbers);

            List<Integer> lottoLine = new ArrayList<>(numbers.subList(0, NUMBERS_PER_LINE));
            Collections.sort(lottoLine);

            lineButtons.get(line - 1).setText(line + ". " + lottoLine);
        }
    }

    public static void main(String[] args) {
        SwingUtilities.invokeLater(() -> new UK49ThreeNumberGeneratorGUI().setVisible(true));
    }
}
